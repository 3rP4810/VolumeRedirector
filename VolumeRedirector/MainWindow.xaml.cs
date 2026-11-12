using System.Windows;
using VolumeRedirector.Models;

namespace VolumeRedirector;

public partial class MainWindow : Window
{
    // DTO locale per la lista
    private record AudioAppEntry(string ProcessName, string DisplayName, int ProcessId);

    public MainWindow()
    {
        InitializeComponent();
        // Icona della finestra impostata da codice: se manca non blocca l'apertura
        if (App.WindowIcon != null) Icon = App.WindowIcon;
        // MinimizeToTrayCheckBox.IsChecked = App.SettingsService.Current.MinimizeToTray;
        // Lo stato arriva dal collegamento vero nella cartella di avvio, non dal file delle impostazioni
        StartWithWindowsCheckBox.IsChecked = App.StartupShortcutService.IsEnabled();
        LoadAppList();
        ShowCurrentStatus();
    }

    // Se l'opzione è attiva, il pulsante Riduci a icona nasconde la finestra nella tray
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized &&
            App.SettingsService.Current.MinimizeToTray)
        {
            Hide();
        }
    }

    /* private void MinimizeToTrayCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.SettingsService.Load();
        settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        App.SettingsService.Save(settings);
    } */

    private void StartWithWindowsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool enable = StartWithWindowsCheckBox.IsChecked == true;
        try
        {
            if (enable)
                App.StartupShortcutService.Create();
            else
                App.StartupShortcutService.Remove();
        }
        catch (Exception ex)
        {
            // L'operazione non è riuscita: la casella torna allo stato reale
            StartWithWindowsCheckBox.IsChecked = !enable;
            MessageBox.Show(
                $"Impossibile {(enable ? "creare" : "rimuovere")} il collegamento nella cartella di avvio.\n\n{ex.Message}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadAppList()
    {
        var sessions = App.AudioService.GetActiveAudioSessions();
        var entries = sessions
            .Select(s => new AudioAppEntry(s.ProcessName, s.DisplayName, s.ProcessId))
            .OrderBy(a => a.DisplayName)
            .ToList();

        AppListBox.ItemsSource = entries;

        // Pre-seleziona l'app già configurata, se presente nella lista
        var settings = App.SettingsService.Load();
        if (!string.IsNullOrEmpty(settings.TargetProcessName))
        {
            var match = entries.FirstOrDefault(e =>
                e.ProcessName.Equals(settings.TargetProcessName,
                                     StringComparison.OrdinalIgnoreCase));
            if (match != null)
                AppListBox.SelectedItem = match;
        }
    }

    private void ShowCurrentStatus()
    {
        var settings = App.SettingsService.Load();
        if (string.IsNullOrEmpty(settings.TargetProcessName))
        {
            StatusText.Text = "Nessuna app selezionata. I tasti volume agiscono sul sistema.";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        }
        else
        {
            bool active = App.AudioService.IsAppActive(settings.TargetProcessName);
            StatusText.Text = active
                ? $"✅ Collegato a: {settings.TargetDisplayName} ({settings.TargetProcessName})"
                : $"⚠️ App salvata \"{settings.TargetDisplayName}\" non è in esecuzione";
            StatusText.Foreground = active
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.OrangeRed;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAppList();
        ShowCurrentStatus();
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppListBox.SelectedItem is not AudioAppEntry selected)
        {
            MessageBox.Show("Seleziona un'applicazione dalla lista.", "Attenzione",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new AppSettings
        {
            TargetProcessName = selected.ProcessName,
            TargetDisplayName = selected.DisplayName,
            VolumeStep = 0.05f,
            // MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true
        };

        App.SettingsService.Save(settings);
        App.HotkeyService.IsActive = true;

        StatusText.Text = $"✅ Collegato a: {selected.DisplayName}";
        StatusText.Foreground = System.Windows.Media.Brushes.Green;

        MessageBox.Show($"Collegato a \"{selected.DisplayName}\".\n" +
                        "I tasti Volume Su/Giù modificheranno il suo volume.",
                        "Impostazioni salvate",
                        MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings // TargetProcessName = null
        {
            // MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true
        };
        App.SettingsService.Save(settings);
        App.HotkeyService.IsActive = false;

        StatusText.Text = "Nessuna app selezionata. I tasti volume agiscono sul sistema.";
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;

        MessageBox.Show("Collegamento rimosso. I tasti volume tornano al comportamento normale.",
                        "Collegamento rimosso",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }
}