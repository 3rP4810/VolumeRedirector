using VolumeRedirector.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Applica il tema (Sistema, Chiaro, Scuro) alle risorse di WPF UI usate dalla finestra delle
    /// impostazioni. Con "Sistema" la finestra aperta segue Windows in tempo reale; con Chiaro o
    /// Scuro l'osservazione dei cambi di Windows si ferma. La flyout non usa WPF UI: legge il tema
    /// da sola (vedi VolumeFlyoutWindow).
    /// </summary>
    public class ThemeService
    {
        private FluentWindow? _window;
        private AppTheme _theme = AppTheme.System;
        private bool _watching;

        /// <summary>
        /// Collega la finestra delle impostazioni: applica il tema salvato e, con "Sistema",
        /// la fa seguire Windows finché resta aperta.
        /// </summary>
        public void Attach(FluentWindow window)
        {
            _window = window;
            _watching = false;

            window.Loaded += (_, _) => UpdateWatching();
            window.Closed += (_, _) =>
            {
                StopWatching();
                if (ReferenceEquals(_window, window)) _window = null;
            };

            Apply(App.SettingsService.Current.Theme);
        }

        /// <summary>Applica subito il tema scelto.</summary>
        public void Apply(AppTheme theme)
        {
            _theme = theme;

            try
            {
                switch (theme)
                {
                    case AppTheme.Light:
                        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                        break;
                    case AppTheme.Dark:
                        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                        break;
                    default:
                        ApplicationThemeManager.ApplySystemTheme(true);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Impossibile applicare il tema", ex);
            }

            UpdateWatching();
        }

        // Osserva i cambi di Windows solo con "Sistema" e solo se la finestra è già aperta
        private void UpdateWatching()
        {
            if (_window is not { IsLoaded: true }) return;

            try
            {
                if (_theme == AppTheme.System && !_watching)
                {
                    SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, true);
                    _watching = true;
                }
                else if (_theme != AppTheme.System && _watching)
                {
                    StopWatching();
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Impossibile aggiornare l'osservazione del tema di Windows", ex);
            }
        }

        private void StopWatching()
        {
            if (!_watching || _window == null) return;

            try
            {
                SystemThemeWatcher.UnWatch(_window);
            }
            catch
            {
                // Finestra già chiusa: niente da fermare
            }

            _watching = false;
        }
    }
}
