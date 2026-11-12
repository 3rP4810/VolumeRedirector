using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Stato della sessione audio di un'app dopo una modifica:
    /// livello del suo cursore nel Mixer volume (0.0 - 1.0) e stato di muto.
    /// </summary>
    public readonly record struct AppAudioState(float Volume, bool IsMuted);

    public class AudioService : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();

        /// <summary>
        /// Restituisce l'elenco dei processi che hanno una sessione audio attiva.
        /// </summary>
        public List<(string ProcessName, string DisplayName, int ProcessId)> GetActiveAudioSessions()
        {
            var result = new List<(string, string, int)>();

            // Ottieni il dispositivo audio predefinito (altoparlante)
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];

                // Salta la sessione di sistema (ProcessId == 0)
                if (session.GetProcessID == 0) continue;

                try
                {
                    using var proc = Process.GetProcessById((int)session.GetProcessID);
                    string procName = proc.ProcessName;

                    // Evita duplicati (stesso processo, più sessioni)
                    if (seen.Contains(procName)) continue;
                    seen.Add(procName);

                    // Prova a recuperare il nome del file eseguibile come display name
                    string displayName = session.DisplayName;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        try { displayName = proc.MainWindowTitle; } catch { }
                    }
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = procName;

                    result.Add((procName, displayName, (int)session.GetProcessID));
                }
                catch
                {
                    // Il processo potrebbe essere già terminato
                }
            }

            return result;
        }

        /// <summary>
        /// Verifica se un'applicazione con il nome del processo specificato
        /// ha una sessione audio attiva in questo momento.
        /// </summary>
        public bool IsAppActive(string processName)
        {
            return FindSession(processName) != null;
        }

        /// <summary>
        /// Restituisce il volume corrente (0.0 - 1.0) dell'applicazione target.
        /// Restituisce null se l'applicazione non è attiva.
        /// </summary>
        public float? GetVolume(string processName)
        {
            var session = FindSession(processName);
            return session?.SimpleAudioVolume.Volume;
        }

        /// <summary>
        /// Cambia il volume dell'applicazione target del valore specificato.
        /// Con delta positivo (Volume Su) toglie anche il muto; con delta negativo
        /// (Volume Giù) il muto resta com'è. Restituisce lo stato dopo la modifica,
        /// oppure null se l'app non ha una sessione audio attiva.
        /// </summary>
        public AppAudioState? AdjustVolume(string processName, float delta)
        {
            var session = FindSession(processName);
            if (session == null) return null;

            try
            {
                var audio = session.SimpleAudioVolume;

                if (delta > 0 && audio.Mute)
                    audio.Mute = false;

                audio.Volume = Math.Clamp(audio.Volume + delta, 0.0f, 1.0f);
                return new AppAudioState(audio.Volume, audio.Mute);
            }
            catch
            {
                // Sessione chiusa nel frattempo
                return null;
            }
        }

        /// <summary>
        /// Inverte il muto dell'applicazione target (solo la sua sessione, non il sistema).
        /// Restituisce lo stato dopo la modifica, oppure null se l'app non ha una sessione attiva.
        /// </summary>
        public AppAudioState? ToggleMute(string processName)
        {
            var session = FindSession(processName);
            if (session == null) return null;

            try
            {
                var audio = session.SimpleAudioVolume;
                audio.Mute = !audio.Mute;
                return new AppAudioState(audio.Volume, audio.Mute);
            }
            catch
            {
                return null;
            }
        }

        private AudioSessionControl? FindSession(string processName)
        {
            try
            {
                using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == 0) continue;

                    try
                    {
                        using var proc = Process.GetProcessById((int)session.GetProcessID);
                        if (proc.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                            return session;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        public void Dispose() => _enumerator.Dispose();
    }
}
