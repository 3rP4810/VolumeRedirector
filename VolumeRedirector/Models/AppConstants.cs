namespace VolumeRedirector.Models
{
    /// <summary>
    /// Costanti da ritoccare in un punto solo.
    /// </summary>
    public static class AppConstants
    {
        // --- Gesti Bluetooth ---

        /// <summary>
        /// Finestra di tempo (ms) dopo che il programma rimette a posto il volume di sistema,
        /// durante la quale le variazioni "di ritorno" vengono ignorate.
        /// </summary>
        public const int SelfChangeIgnoreMs = 400;

        /// <summary>
        /// Nella finestra di sopra, una variazione si considera causata dal programma solo se il
        /// livello è entro questa distanza (0.0 - 1.0) da quello impostato dal programma.
        /// Il Bluetooth arrotonda il volume, quindi serve un po' di tolleranza.
        /// </summary>
        public const float SelfChangeTolerance = 0.02f;

        /// <summary>
        /// Variazioni di livello più piccole di questo valore (0.0 - 1.0) non sono un gesto:
        /// sono il solo cambio del muto di sistema o rumore.
        /// </summary>
        public const float MinLevelChange = 0.001f;

        /// <summary>Sotto questa percentuale il Volume Giù delle cuffie non viene rilevato.</summary>
        public const double LowLevelWarnPercent = 2;

        /// <summary>Sopra questa percentuale il Volume Su delle cuffie non viene rilevato.</summary>
        public const double HighLevelWarnPercent = 98;

        // --- Log locale ---

        /// <summary>Quanti giorni di file di log si conservano.</summary>
        public const int LogKeepDays = 14;
    }
}
