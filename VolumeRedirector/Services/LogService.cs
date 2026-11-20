using System.Globalization;
using System.IO;
using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using VolumeRedirector.Models;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Log locale con NLog: un file al giorno in %APPDATA%\VolumeRedirector\logs, conservato per
    /// AppConstants.LogKeepDays giorni. Si scrivono solo nomi di processi e app: nessun altro dato
    /// personale (nei testi delle eccezioni il profilo utente e il nome utente sono mascherati).
    /// Un errore del log non deve mai fermare il programma.
    /// </summary>
    public static class LogService
    {
        private const string FilePrefix = "volume-redirector-";
        private const string FileExtension = ".log";

        private static readonly string LogDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "VolumeRedirector", "logs");

        private static readonly object CleanupLock = new();
        private static Logger? _logger;
        private static DateTime _lastCleanupDay = DateTime.MinValue;

        /// <summary>
        /// Prepara il log (solo nell'istanza principale). Senza questa chiamata i metodi non fanno nulla.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                var file = new FileTarget("file")
                {
                    FileName = Path.Combine(LogDir, FilePrefix + "${shortdate}" + FileExtension),
                    Layout = "${longdate} [${level:uppercase=true}] ${message}",
                    Encoding = new UTF8Encoding(false),
                    KeepFileOpen = false,
                    CreateDirs = true
                };

                // Scrittura asincrona: il log non rallenta l'hook della tastiera
                var async = new AsyncTargetWrapper("fileAsync", file)
                {
                    OverflowAction = AsyncTargetWrapperOverflowAction.Discard,
                    QueueLimit = 1000
                };

                var config = new LoggingConfiguration();
                config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, async);
                LogManager.Configuration = config;

                _logger = LogManager.GetLogger("VolumeRedirector");
                CleanOldLogs();
            }
            catch
            {
                _logger = null;
            }
        }

        public static void Info(string message) => Write(NLog.LogLevel.Info, message);

        public static void Warn(string message) => Write(NLog.LogLevel.Warn, message);

        public static void Error(string message, Exception? exception = null) =>
            Write(NLog.LogLevel.Error, exception == null ? message : $"{message}: {exception}");

        /// <summary>Scrive su disco quanto è ancora in coda (da usare prima di un'uscita improvvisa).</summary>
        public static void Flush()
        {
            try { LogManager.Flush(TimeSpan.FromSeconds(2)); } catch { }
        }

        /// <summary>Chiude il log scrivendo quanto è ancora in coda.</summary>
        public static void Shutdown()
        {
            try
            {
                if (_logger == null) return;
                LogManager.Shutdown();
                _logger = null;
            }
            catch { }
        }

        private static void Write(NLog.LogLevel level, string message)
        {
            try
            {
                var logger = _logger;
                if (logger == null) return;

                // Alla prima riga di un nuovo giorno si ripulisce la cartella (il programma può restare aperto a lungo)
                if (_lastCleanupDay != DateTime.Today)
                    Task.Run(CleanOldLogs);

                logger.Log(level, Sanitize(message));
            }
            catch
            {
                // Il log è solo un supporto: un errore qui non deve fermare il programma
            }
        }

        private static void CleanOldLogs()
        {
            lock (CleanupLock)
            {
                try
                {
                    DateTime today = DateTime.Today;
                    if (_lastCleanupDay == today) return;
                    _lastCleanupDay = today;

                    // Si tengono oggi e i giorni precedenti fino a LogKeepDays in totale
                    DateTime limit = today.AddDays(-AppConstants.LogKeepDays);
                    foreach (string path in Directory.EnumerateFiles(LogDir, FilePrefix + "*" + FileExtension))
                    {
                        DateTime day = ParseDay(path) ?? File.GetLastWriteTime(path).Date;
                        if (day <= limit)
                        {
                            try { File.Delete(path); } catch { }
                        }
                    }
                }
                catch
                {
                    // Cartella non leggibile: si riprova il giorno dopo
                }
            }
        }

        // Data dal nome del file (volume-redirector-2026-11-19.log)
        private static DateTime? ParseDay(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length <= FilePrefix.Length) return null;

            return DateTime.TryParseExact(name[FilePrefix.Length..], "yyyy-MM-dd",
                                          CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
                ? day
                : null;
        }

        // Maschera cartella del profilo e nome utente, che possono comparire nei testi delle eccezioni
        private static string Sanitize(string text)
        {
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                    text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user))
                    text = text.Replace(user, "<utente>", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Si scrive il testo com'è
            }

            return text;
        }
    }
}
