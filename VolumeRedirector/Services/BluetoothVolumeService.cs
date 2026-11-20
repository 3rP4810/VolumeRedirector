using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VolumeRedirector.Models;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Direzione di un gesto Volume Su / Volume Giù.
    /// </summary>
    public enum VolumeDirection
    {
        Up,
        Down
    }

    /// <summary>
    /// Osserva il volume dell'uscita audio predefinita e i cambi di dispositivo predefinito.
    /// I gesti delle cuffie Bluetooth non sono tasti: Windows li riceve come variazioni del volume
    /// del dispositivo. Le notifiche arrivano su thread di sistema e vengono portate sul thread
    /// dell'interfaccia prima di toccare audio o interfaccia: GestureHandler ed EdgeLevelHandler
    /// sono chiamati sempre sul thread UI.
    /// </summary>
    public class BluetoothVolumeService : IDisposable
    {
        // Pezzi di testo che identificano un'uscita Bluetooth nelle proprietà del dispositivo
        // (percorso dell'interfaccia o ID di istanza: BTHENUM, BTHHFENUM, BTHLEDEVICE...)
        private static readonly string[] BluetoothMarkers =
            { "BTHENUM", "BTHHFENUM", "BTHLE", "BTHA2DP", "BTHPORT" };

        private readonly MMDeviceEnumerator _enumerator = new();

        private Dispatcher? _dispatcher;
        private NotificationClient? _client;
        private MMDevice? _device;
        private string? _deviceId;
        private AudioEndpointVolumeNotificationDelegate? _volumeHandler;
        private bool _isBluetooth;
        private bool _levelWarned;      // l'avviso sul livello estremo è già stato mostrato per questa connessione
        private float _lastLevel;       // ultimo livello noto del volume di sistema (0.0 - 1.0)
        private float _selfLevel;       // ultimo livello impostato dal programma
        private long _ignoreUntilTick;  // fino a quando si ignorano le variazioni "di ritorno"
        private int _generation;        // cambia a ogni dispositivo: scarta le notifiche in ritardo
        private bool _started;
        private bool _disposed;

        /// <summary>
        /// True se il programma deve agire sui gesti (interruttore acceso e app salvata).
        /// Letto a ogni evento, sul thread UI.
        /// </summary>
        public Func<bool>? ShouldAct { get; set; }

        /// <summary>
        /// Chiamato (thread UI) a ogni gesto. Restituisce true se il gesto è stato gestito
        /// sull'app collegata: il volume di sistema viene allora rimesso all'ultimo valore noto.
        /// Con false il nuovo livello diventa l'ultimo noto e non si rimette nulla a posto.
        /// </summary>
        public Func<VolumeDirection, bool>? GestureHandler { get; set; }

        /// <summary>
        /// Chiamato (thread UI), una sola volta per connessione, con la percentuale di volume di
        /// sistema quando questa è sopra o sotto le soglie e i gesti non si possono rilevare.
        /// </summary>
        public Action<int>? EdgeLevelHandler { get; set; }

        /// <summary>
        /// Inizia a osservare. Con checkLevelAtStart false l'avviso sul livello estremo non viene
        /// valutato per l'uscita già presente (avvio automatico: nessun avviso).
        /// </summary>
        public void Start(Dispatcher dispatcher, bool checkLevelAtStart)
        {
            if (_started || _disposed) return;
            _started = true;
            _dispatcher = dispatcher;

            try
            {
                _client = new NotificationClient(this);
                _enumerator.RegisterEndpointNotificationCallback(_client);
            }
            catch (Exception ex)
            {
                LogService.Error("Impossibile osservare i cambi di dispositivo audio", ex);
            }

            Attach(force: true, checkLevel: checkLevelAtStart);
        }

        /// <summary>
        /// Da chiamare (thread UI) dopo che l'interruttore o l'app salvata sono cambiati: se si sta
        /// già osservando un'uscita Bluetooth con volume estremo, l'avviso compare ora.
        /// </summary>
        public void Reevaluate()
        {
            if (_started && !_disposed) CheckEdgeLevel();
        }

        // -------------------------
        // Dispositivo predefinito
        // -------------------------

        private void Attach(bool force, bool checkLevel = true)
        {
            if (_disposed) return;

            MMDevice? newDevice = null;
            string? newId = null;
            try
            {
                newDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                newId = newDevice.ID;
            }
            catch
            {
                // Nessuna uscita audio disponibile
                newDevice?.Dispose();
                newDevice = null;
            }

            // Stesso dispositivo già osservato: niente da rifare (e niente nuova "connessione")
            if (!force && newDevice != null && newId == _deviceId)
            {
                newDevice.Dispose();
                return;
            }

            Detach();

            if (newDevice == null)
            {
                LogService.Info("Nessuna uscita audio predefinita");
                return;
            }

            int generation = ++_generation;
            _device = newDevice;
            _deviceId = newId;
            _isBluetooth = IsBluetoothDevice(newDevice);
            _levelWarned = false;

            try
            {
                var volume = newDevice.AudioEndpointVolume;
                _lastLevel = volume.MasterVolumeLevelScalar;

                // Notifica su thread di sistema: si passa al thread UI prima di fare altro
                _volumeHandler = data =>
                {
                    float level = data.MasterVolume;
                    _dispatcher?.InvokeAsync(() => OnVolumeNotification(generation, level));
                };
                volume.OnVolumeNotification += _volumeHandler;
            }
            catch (Exception ex)
            {
                LogService.Error("Impossibile osservare il volume dell'uscita audio", ex);
                _isBluetooth = false;
            }

            LogService.Info(_isBluetooth
                ? "Uscita audio predefinita: Bluetooth"
                : "Uscita audio predefinita: non Bluetooth");

            if (checkLevel) CheckEdgeLevel();
        }

        private void Detach()
        {
            _generation++;

            try
            {
                if (_device != null && _volumeHandler != null)
                    _device.AudioEndpointVolume.OnVolumeNotification -= _volumeHandler;
            }
            catch { }

            try { _device?.Dispose(); } catch { }

            _device = null;
            _deviceId = null;
            _volumeHandler = null;
            _isBluetooth = false;
        }

        // Bluetooth se una qualunque proprietà testuale del dispositivo contiene uno dei marcatori
        private static bool IsBluetoothDevice(MMDevice device)
        {
            try
            {
                var properties = device.Properties;
                for (int i = 0; i < properties.Count; i++)
                {
                    try
                    {
                        if (properties[i].Value is string text && ContainsBluetoothMarker(text))
                            return true;
                    }
                    catch
                    {
                        // Proprietà di un tipo non leggibile: si passa alla successiva
                    }
                }
            }
            catch
            {
                // Proprietà non leggibili: non si considera Bluetooth
            }

            return false;
        }

        private static bool ContainsBluetoothMarker(string text)
        {
            foreach (string marker in BluetoothMarkers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // -------------------------
        // Variazioni del volume
        // -------------------------

        // Thread UI
        private void OnVolumeNotification(int generation, float level)
        {
            if (_disposed || generation != _generation) return;

            float previous = _lastLevel;

            // Variazione di livello trascurabile: è cambiato solo il muto di sistema, si ignora
            if (Math.Abs(level - previous) < AppConstants.MinLevelChange) return;

            // Variazione causata dal programma stesso (volume rimesso a posto): si ignora
            if (Environment.TickCount64 < _ignoreUntilTick &&
                Math.Abs(level - _selfLevel) <= AppConstants.SelfChangeTolerance)
            {
                _lastLevel = level;
                return;
            }

            // Si agisce solo con uscita Bluetooth, interruttore acceso e app salvata.
            // Negli altri casi si tiene solo aggiornato l'ultimo livello noto.
            if (!_isBluetooth || ShouldAct?.Invoke() != true)
            {
                _lastLevel = level;
                return;
            }

            var direction = level > previous ? VolumeDirection.Up : VolumeDirection.Down;

            bool handled = false;
            try
            {
                handled = GestureHandler?.Invoke(direction) ?? false;
            }
            catch (Exception ex)
            {
                LogService.Error("Errore nella gestione del gesto Bluetooth", ex);
            }

            if (handled)
                RestoreLevel(previous);   // il volume di sistema non deve cambiare
            else
                _lastLevel = level;       // gesto passato a Windows: nuovo livello noto
        }

        // Rimette il volume di sistema a un valore, segnando la variazione come "propria"
        private void RestoreLevel(float level)
        {
            if (_device == null) return;

            _selfLevel = level;
            _ignoreUntilTick = Environment.TickCount64 + AppConstants.SelfChangeIgnoreMs;

            try
            {
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = level;
                _lastLevel = level;
            }
            catch (Exception ex)
            {
                LogService.Error("Impossibile rimettere il volume di sistema", ex);
                _ignoreUntilTick = 0;
            }
        }

        // Avviso (una volta per connessione) se il volume di sistema è così alto o basso
        // che il Volume Su o Giù delle cuffie non produce nessuna variazione
        private void CheckEdgeLevel()
        {
            if (_levelWarned || !_isBluetooth || _device == null) return;
            if (ShouldAct?.Invoke() != true) return;

            float level;
            try
            {
                level = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch
            {
                return;
            }

            double percent = level * 100.0;
            if (percent > AppConstants.HighLevelWarnPercent || percent < AppConstants.LowLevelWarnPercent)
            {
                _levelWarned = true;
                _lastLevel = level;
                EdgeLevelHandler?.Invoke((int)Math.Round(percent));
            }
        }

        // -------------------------
        // Notifiche di sistema (thread di sistema: si passa subito al thread UI)
        // -------------------------

        private void Post(Action action)
        {
            _dispatcher?.InvokeAsync(() =>
            {
                if (_disposed) return;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    LogService.Error("Errore nell'osservazione dell'uscita audio", ex);
                }
            });
        }

        private void OnDefaultDeviceChanged() => Post(() => Attach(force: false));

        private void OnDeviceStateChanged(string deviceId, DeviceState state) => Post(() =>
        {
            // Il dispositivo osservato non è più attivo (cuffie scollegate): si smette di osservarlo
            if (deviceId == _deviceId && state != DeviceState.Active)
                Detach();

            // Ricollegamento o nuovo predefinito: si osserva quello attuale
            Attach(force: false);
        });

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_client != null)
                    _enumerator.UnregisterEndpointNotificationCallback(_client);
            }
            catch { }

            Detach();

            try { _enumerator.Dispose(); } catch { }
        }

        // Riceve le notifiche di NAudio sui dispositivi (thread di sistema)
        private sealed class NotificationClient : IMMNotificationClient
        {
            private readonly BluetoothVolumeService _owner;

            public NotificationClient(BluetoothVolumeService owner) => _owner = owner;

            public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
                _owner.OnDeviceStateChanged(deviceId, newState);

            public void OnDeviceAdded(string pwstrDeviceId) { }

            public void OnDeviceRemoved(string deviceId) { }

            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                // Come AudioService: conta l'uscita predefinita per audio e media
                if (flow == DataFlow.Render && role == Role.Multimedia)
                    _owner.OnDefaultDeviceChanged();
            }

            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}
