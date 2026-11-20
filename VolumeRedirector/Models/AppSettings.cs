using System.Text.Json.Serialization;

namespace VolumeRedirector.Models
{
    /// <summary>
    /// Tema della finestra delle impostazioni e della flyout.
    /// L'ordine dei valori coincide con quello delle voci del selettore nella finestra.
    /// </summary>
    public enum AppTheme
    {
        System,
        Light,
        Dark
    }

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
        /// Se true, i gesti Volume Su/Giù di un'uscita audio Bluetooth (che Windows riceve come
        /// variazioni del volume del dispositivo) agiscono sull'app collegata. Default: true.
        /// </summary>
        public bool InterceptBluetooth { get; set; } = true;

        /// <summary>
        /// Tema di finestra e flyout: Sistema (segue Windows), Chiaro o Scuro. Default: Sistema.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AppTheme Theme { get; set; } = AppTheme.System;

        /// <summary>
        /// Copia indipendente delle impostazioni.
        /// </summary>
        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }
}
