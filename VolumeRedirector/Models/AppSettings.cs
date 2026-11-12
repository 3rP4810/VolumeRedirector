namespace VolumeRedirector.Models
{
    public class AppSettings
    {
        /// <summary>
        /// Nome del processo dell'applicazione target (es. "chrome", "spotify").
        /// </summary>
        public string? TargetProcessName { get; set; }

        /// <summary>
        /// Nome leggibile dell'applicazione target (per mostrarla all'utente).
        /// </summary>
        public string? TargetDisplayName { get; set; }

        /// <summary>
        /// Passo di volume per ogni pressione del tasto (0.0 - 1.0).
        /// Default: 0.05 (5%).
        /// </summary>
        public float VolumeStep { get; set; } = 0.05f;

        /// <summary>
        /// Se true, il pulsante "Riduci a icona" nasconde la finestra nell'area di
        /// notifica (tray) invece di lasciarla sulla barra delle applicazioni.
        /// Non influisce sull'avvio. Default: false.
        /// </summary>
        public bool MinimizeToTray { get; set; } = false;

        /// <summary>
        /// Copia indipendente delle impostazioni.
        /// </summary>
        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }
}
