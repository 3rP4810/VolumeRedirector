using System.IO;
using System.Text.Json;
using VolumeRedirector.Models;

namespace VolumeRedirector.Services
{
    public class SettingsService
    {
        // Salva le impostazioni nella cartella AppData dell'utente
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "VolumeRedirector");

        private static readonly string SettingsPath =
            Path.Combine(SettingsDir, "settings.json");

        private readonly object _lock = new();
        private AppSettings? _current;

        /// <summary>
        /// Impostazioni correnti tenute in memoria: il file viene letto una sola volta
        /// e la copia in memoria si aggiorna a ogni Save. Da usare in sola lettura
        /// (es. dentro l'hook della tastiera, dove non si può leggere il disco a ogni tasto).
        /// </summary>
        public AppSettings Current
        {
            get
            {
                lock (_lock)
                {
                    return _current ??= ReadFromDisk();
                }
            }
        }

        /// <summary>
        /// Restituisce una copia modificabile delle impostazioni correnti.
        /// </summary>
        public AppSettings Load() => Current.Clone();

        private static AppSettings ReadFromDisk()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);

            lock (_lock)
            {
                _current = settings.Clone();
            }
        }
    }
}
