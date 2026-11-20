using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VolumeRedirector.Models;
using VolumeRedirector.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace VolumeRedirector;

public partial class MainWindow : FluentWindow
{
    // Durata delle notifiche in basso
    private const int SnackbarSeconds = 4;

    private static readonly Color StatusGreen = Color.FromRgb(0x2E, 0x9E, 0x5B);
    private static readonly Color StatusGray = Color.FromRgb(0x8A, 0x8A, 0x8A);
    private static readonly Color StatusYellow = Color.FromRgb(0xE0, 0xA8, 0x00);

    // DTO locale per la lista (icona già letta, con cache condivisa con la flyout)
    private record AudioAppEntry(string ProcessName, string DisplayName, int ProcessId, ImageSource? Icon)
    {
        public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackVisibility => Icon != null ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly SnackbarService _snackbar = new();
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        // Icona della finestra impostata da codice: se manca non blocca l'apertura
        if (App.WindowIcon != null) Icon = App.WindowIcon;

        _snackbar.SetSnackbarPresenter(SnackbarPresenter);

        // Lo stato arriva dal collegamento vero nella cartella di avvio, non dal file delle impostazioni.
        // Durante il caricamento il cambio del tema non deve scatenare il salvataggio.
        _loading = true;
        var settings = App.SettingsService.Load();
        StartWithWindowsToggle.IsChecked = App.StartupShortcutService.IsEnabled();
        InterceptBluetoothToggle.IsChecked = settings.InterceptBluetooth;
        ThemeComboBox.SelectedIndex = (int)settings.Theme;
        _loading = false;

        // Tema salvato; con "Sistema" la finestra segue Windows in tempo reale
        App.ThemeService.Attach(this);

        LoadAppList();
        ShowCurrentStatus();
    }

    private void StartWithWindowsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enable = StartWithWindowsToggle.IsChecked == true;
        try
        {
            if (enable)
                App.StartupShortcutService.Create();
            else
                App.StartupShortcutService.Remove();

            LogService.Info($"Avvia con Windows: {(enable ? "acceso" : "spento")}");
            ShowSnackbar("Avvia con Windows",
                         enable ? "Il programma partirà con Windows." : "Il programma non partirà più con Windows.",
                         ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
        }
        catch (Exception ex)
        {
            // L'operazione non è riuscita: l'interruttore torna allo stato reale
            StartWithWindowsToggle.IsChecked = !enable;
            LogService.Error($"Avvia con Windows: impossibile {(enable ? "creare" : "rimuovere")} il collegamento", ex);
            ShowSnackbar("Errore",
                         $"Impossibile {(enable ? "creare" : "rimuovere")} il collegamento nella cartella di avvio. {ex.Message}",
                         ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
        }
    }

    private void InterceptBluetoothToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enable = InterceptBluetoothToggle.IsChecked == true;
        try
        {
            var settings = App.SettingsService.Load();
            settings.InterceptBluetooth = enable;
            App.SettingsService.Save(settings);

            LogService.Info($"Intercetta il volume dei dispositivi Bluetooth: {(enable ? "acceso" : "spento")}");

            // Con l'uscita Bluetooth già collegata, l'avviso sul livello può comparire ora
            App.BluetoothVolumeService.Reevaluate();

            ShowSnackbar("Dispositivi Bluetooth",
                         enable ? "Intercettazione del volume Bluetooth attivata." : "Intercettazione del volume Bluetooth disattivata.",
                         ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
        }
        catch (Exception ex)
        {
            InterceptBluetoothToggle.IsChecked = !enable;
            LogService.Error("Impossibile salvare l'impostazione Bluetooth", ex);
            ShowSnackbar("Errore", $"Impossibile salvare l'impostazione. {ex.Message}",
                         ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeComboBox.SelectedIndex < 0) return;

        var theme = (AppTheme)ThemeComboBox.SelectedIndex;
        try
        {
            var settings = App.SettingsService.Load();
            if (settings.Theme == theme) return;

            settings.Theme = theme;
            App.SettingsService.Save(settings);

            // Il cambio si applica subito alla finestra; la flyout lo legge alla prossima apparizione
            App.ThemeService.Apply(theme);

            LogService.Info($"Tema impostato: {ThemeName(theme)}");
            ShowSnackbar("Tema", $"Tema impostato: {ThemeName(theme)}.",
                         ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
        }
        catch (Exception ex)
        {
            LogService.Error("Impossibile salvare il tema", ex);
            ShowSnackbar("Errore", $"Impossibile salvare il tema. {ex.Message}",
                         ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
        }
    }

    private static string ThemeName(AppTheme theme) => theme switch
    {
        AppTheme.Light => "Chiaro",
        AppTheme.Dark => "Scuro",
        _ => "Sistema"
    };

    private void LoadAppList()
    {
        List<AudioAppEntry> entries;
        try
        {
            var sessions = App.AudioService.GetActiveAudioSessions();
            entries = sessions
                .Select(s => new AudioAppEntry(s.ProcessName, s.DisplayName, s.ProcessId,
                                               App.GetProcessIcon(s.ProcessName)))
                .OrderBy(a => a.DisplayName)
                .ToList();
        }
        catch (Exception ex)
        {
            // Nessuna uscita audio disponibile: elenco vuoto
            LogService.Error("Impossibile leggere le sessioni audio", ex);
            entries = new List<AudioAppEntry>();
        }

        AppListBox.ItemsSource = entries;
        EmptyListText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

    // Barra di stato: verde (app collegata), grigia (nessuna app), gialla (collegata ma non in esecuzione)
    private void ShowCurrentStatus()
    {
        var settings = App.SettingsService.Load();
        if (string.IsNullOrEmpty(settings.TargetProcessName))
        {
            SetStatus(StatusGray, "Nessuna app selezionata. I tasti volume agiscono sul sistema.");
            return;
        }

        string name = string.IsNullOrWhiteSpace(settings.TargetDisplayName)
            ? settings.TargetProcessName
            : settings.TargetDisplayName;

        if (App.AudioService.IsAppActive(settings.TargetProcessName))
        {
            SetStatus(StatusGreen, $"Collegato a: {name} ({settings.TargetProcessName})");
        }
        else if (App.IsProcessRunning(settings.TargetProcessName))
        {
            SetStatus(StatusYellow, $"App salvata \"{name}\" è aperta ma non sta riproducendo audio");
        }
        else
        {
            SetStatus(StatusYellow, $"App salvata \"{name}\" non è in esecuzione");
        }
    }

    private void SetStatus(Color color, string text)
    {
        StatusDot.Fill = new SolidColorBrush(color);
        StatusBar.Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
        StatusText.Text = text;
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
            ShowSnackbar("Attenzione", "Seleziona un'applicazione dalla lista.",
                         ControlAppearance.Caution, SymbolRegular.Warning24);
            return;
        }

        try
        {
            // Si cambia solo l'app scelta: le altre impostazioni (passo compreso) restano come sono
            var settings = App.SettingsService.Load();
            settings.TargetProcessName = selected.ProcessName;
            settings.TargetDisplayName = selected.DisplayName;

            App.SettingsService.Save(settings);
            App.HotkeyService.IsActive = true;
        }
        catch (Exception ex)
        {
            LogService.Error("Impossibile salvare l'app scelta", ex);
            ShowSnackbar("Errore", $"Impossibile salvare le impostazioni. {ex.Message}",
                         ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            return;
        }

        LogService.Info($"App collegata: {selected.DisplayName} ({selected.ProcessName})");

        // Con l'uscita Bluetooth già collegata, l'avviso sul livello può comparire ora
        App.BluetoothVolumeService.Reevaluate();

        ShowCurrentStatus();

        // La finestra resta aperta
        ShowSnackbar("Impostazioni salvate",
                     $"Collegato a \"{selected.DisplayName}\". I tasti Volume Su/Giù modificheranno il suo volume.",
                     ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Si toglie solo l'app: le altre impostazioni (passo compreso) restano come sono
            var settings = App.SettingsService.Load();
            settings.TargetProcessName = null;
            settings.TargetDisplayName = null;

            App.SettingsService.Save(settings);
            App.HotkeyService.IsActive = false;
        }
        catch (Exception ex)
        {
            LogService.Error("Impossibile rimuovere il collegamento", ex);
            ShowSnackbar("Errore", $"Impossibile salvare le impostazioni. {ex.Message}",
                         ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            return;
        }

        LogService.Info("Collegamento rimosso");

        AppListBox.SelectedItem = null;
        ShowCurrentStatus();

        ShowSnackbar("Collegamento rimosso",
                     "I tasti volume tornano al comportamento normale.",
                     ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    }

    // Notifica in basso nella finestra (al posto delle finestre di messaggio)
    private void ShowSnackbar(string title, string message, ControlAppearance appearance, SymbolRegular symbol)
    {
        try
        {
            _snackbar.Show(title, message, appearance, new SymbolIcon(symbol),
                           TimeSpan.FromSeconds(SnackbarSeconds));
        }
        catch (Exception ex)
        {
            LogService.Error("Impossibile mostrare la notifica", ex);
        }
    }
}
