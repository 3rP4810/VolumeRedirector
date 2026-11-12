using System.IO;
using System.Runtime.InteropServices;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Crea e rimuove il collegamento nella cartella di avvio dell'utente (shell:startup)
    /// che lancia il programma con l'argomento --run. Non servono permessi di
    /// amministratore e non si tocca il registro.
    /// </summary>
    public class StartupShortcutService
    {
        private const string ShortcutFileName = "VolumeRedirector.lnk";
        private const string RunArgument = "--run";

        private static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                         ShortcutFileName);

        private static string CurrentExePath =>
            Environment.ProcessPath
            ?? throw new InvalidOperationException("Percorso dell'eseguibile non disponibile.");

        /// <summary>
        /// True se il collegamento esiste e punta a questo eseguibile con --run.
        /// Se il collegamento esiste ma punta altrove (per esempio dopo aver spostato
        /// l'exe), restituisce false: spuntando di nuovo la casella viene ricreato.
        /// </summary>
        public bool IsEnabled()
        {
            try
            {
                if (!File.Exists(ShortcutPath)) return false;

                dynamic shell = CreateShell();
                try
                {
                    dynamic link = shell.CreateShortcut(ShortcutPath);
                    try
                    {
                        string target = (string)link.TargetPath;
                        string args = ((string)link.Arguments).Trim();

                        return string.Equals(target, CurrentExePath, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(args, RunArgument, StringComparison.OrdinalIgnoreCase);
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject((object)link);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject((object)shell);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Crea (o sovrascrive) il collegamento verso questo eseguibile con --run.
        /// </summary>
        public void Create()
        {
            string exe = CurrentExePath;

            dynamic shell = CreateShell();
            try
            {
                dynamic link = shell.CreateShortcut(ShortcutPath);
                try
                {
                    link.TargetPath = exe;
                    link.Arguments = RunArgument;
                    link.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
                    link.Description = "Volume Redirector";
                    link.IconLocation = exe + ",0";
                    link.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject((object)link);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject((object)shell);
            }
        }

        /// <summary>
        /// Elimina il collegamento, se esiste.
        /// </summary>
        public void Remove()
        {
            if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);
        }

        private static dynamic CreateShell()
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Componente WScript.Shell non disponibile.");
            return Activator.CreateInstance(type)!;
        }
    }
}
