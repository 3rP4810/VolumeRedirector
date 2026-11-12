using System.Runtime.InteropServices;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Garantisce una sola istanza del programma per utente e permette a una seconda
    /// istanza di chiedere a quella già attiva di mostrare la finestra.
    /// </summary>
    public class SingleInstanceService : IDisposable
    {
        // "Local\" = oggetti visibili solo nella sessione dell'utente corrente
        private const string MutexName = @"Local\VolumeRedirector.SingleInstance";
        private const string ShowEventName = @"Local\VolumeRedirector.ShowWindow";

        // ASFW_ANY: permette a qualsiasi processo di portarsi in primo piano
        private const int ASFW_ANY = -1;

        private Mutex? _mutex;
        private EventWaitHandle? _showEvent;
        private ManualResetEvent? _stopEvent;
        private Thread? _listenerThread;

        /// <summary>
        /// Scatta (su un thread in background) quando una seconda istanza chiede di
        /// mostrare la finestra. Chi si iscrive deve passare al thread dell'interfaccia.
        /// </summary>
        public event Action? ShowRequested;

        /// <summary>
        /// Restituisce true se questa è la prima istanza (e quindi quella che resta attiva),
        /// false se ne esiste già un'altra.
        /// </summary>
        public bool TryBecomePrimary()
        {
            // L'evento si crea prima del mutex: se un'altra istanza vede il mutex,
            // l'evento esiste già e il segnale non va perso.
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _mutex = new Mutex(false, MutexName, out bool createdNew);

            if (!createdNew)
                return false;

            StartListening();
            return true;
        }

        /// <summary>
        /// Chiede all'istanza già attiva di mostrare la finestra (da usare solo
        /// quando TryBecomePrimary ha restituito false).
        /// </summary>
        public void SignalPrimary()
        {
            // Questa istanza è appena stata avviata dall'utente, quindi può cedere
            // il primo piano all'istanza attiva (altrimenti Windows lo impedirebbe).
            AllowSetForegroundWindow(ASFW_ANY);
            _showEvent?.Set();
        }

        private void StartListening()
        {
            _stopEvent = new ManualResetEvent(false);
            var handles = new WaitHandle[] { _showEvent!, _stopEvent };

            _listenerThread = new Thread(() =>
            {
                try
                {
                    while (WaitHandle.WaitAny(handles) == 0)
                    {
                        ShowRequested?.Invoke();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Chiusura del programma
                }
            })
            {
                IsBackground = true,
                Name = "VolumeRedirector.SingleInstance"
            };
            _listenerThread.Start();
        }

        public void Dispose()
        {
            _stopEvent?.Set();
            _listenerThread?.Join(500);

            _stopEvent?.Dispose();
            _showEvent?.Dispose();
            _mutex?.Dispose();
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
    }
}
