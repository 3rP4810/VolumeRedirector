using System.Diagnostics;
using System.Windows;
using VolumeRedirector.Services;

namespace VolumeRedirector;

public partial class App : Application
{
    public static HotkeyService HotkeyService { get; } = new();
    public static AudioService AudioService { get; } = new();
    public static SettingsService SettingsService { get; } = new();
    public static StartupShortcutService StartupShortcutService { get; } = new();

    // Intervallo minimo tra due avvisi mostrati alla pressione dei tasti volume
    private const long KeyNoticeIntervalMs = 10_000;

    /// <summary>Icona per le finestre (null se non disponibile).</summary>
    public static System.Windows.Media.ImageSource? WindowIcon { get; private set; }

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private System.Drawing.Icon? _appIcon;
    private SingleInstanceService? _singleInstance;
    private long _lastKeyNoticeTick = -1;

    // Flyout dell'app collegata (creata alla prima occorrenza) e icona del processo in cache
    private VolumeFlyoutWindow? _flyout;
    private string? _flyoutIconFor;
    private System.Windows.Media.ImageSource? _flyoutIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        // "--run" = avvio automatico (collegamento in shell:startup): solo icona in tray,
        // nessuna finestra e nessun avviso. Qualsiasi altro caso è un avvio manuale.
        bool runMode = e.Args.Any(a => a.Equals("--run", StringComparison.OrdinalIgnoreCase));

        // Una sola istanza per utente
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryBecomePrimary())
        {
            // Avvio manuale: l'istanza già attiva mostra la sua finestra.
            // Secondo --run: si chiude in silenzio.
            if (!runMode)
                _singleInstance.SignalPrimary();

            Shutdown();
            return;
        }

        base.OnStartup(e);

        // L'app resta attiva nella tray anche senza finestre: si chiude solo da "Esci"
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // L'hook gestisce i tasti volume solo se esiste un'app collegata salvata
        HotkeyService.IsActive = !string.IsNullOrEmpty(SettingsService.Current.TargetProcessName);

        // Icona personalizzata (risorsa incorporata); se non si carica, ripiega su quella di sistema
        _appIcon = LoadAppIcon();
        WindowIcon = CreateWindowIcon(_appIcon);

        // ✅ FIX 1: Usa SystemIcons.Application invece di SystemIcons.Speaker
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Volume Redirector",
            Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        // Registra il click sul balloon (notifica WinForms)
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowMainWindow);

        HotkeyService.VolumeKeyHandler = OnVolumeKey;
        HotkeyService.Install();

        // Una seconda istanza (avvio manuale) chiede di mostrare la finestra
        _singleInstance.ShowRequested += () => Dispatcher.InvokeAsync(ShowMainWindow);

        // Solo in avvio manuale: avviso se l'app salvata non è attiva e finestra aperta.
        // In avvio automatico (--run) non compare nulla.
        if (!runMode)
        {
            CheckSavedAppOnStartup();
            ShowMainWindow();
        }
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        // 1) Risorsa incorporata nell'assembly (Build Action = Resource)
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/volume-adjustment.ico"));
            if (info != null) return new System.Drawing.Icon(info.Stream);
        }
        catch
        {
            // Risorsa non trovata: si prova con l'icona dell'exe
        }

        // 2) Icona dell'eseguibile (ApplicationIcon nel .csproj)
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe != null) return System.Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // Nessuna icona disponibile: si usa quella di sistema
        }

        return null;
    }

    private static System.Windows.Media.ImageSource? CreateWindowIcon(System.Drawing.Icon? icon)
    {
        if (icon == null) return null;
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    // Chiamato dall'hook a ogni pressione di Volume Su, Volume Giù o Volume Muto.
    // Restituisce true se il tasto è stato gestito (bloccato), false se va a Windows.
    private bool OnVolumeKey(VolumeKey key)
    {
        // Impostazioni in memoria: dentro l'hook non si legge il disco
        var settings = SettingsService.Current;
        string processName = settings.TargetProcessName ?? string.Empty;
        if (processName.Length == 0) return false;

        string displayName = string.IsNullOrWhiteSpace(settings.TargetDisplayName)
            ? processName
            : settings.TargetDisplayName;

        // Controllo della sessione audio in diretta: i metodi restituiscono null
        // se l'app non ha una sessione audio attiva.
        AppAudioState? state = key switch
        {
            VolumeKey.Up => AudioService.AdjustVolume(processName, settings.VolumeStep),
            VolumeKey.Down => AudioService.AdjustVolume(processName, -settings.VolumeStep),
            _ => AudioService.ToggleMute(processName)
        };

        if (state is { } appState)
        {
            // Gestito sull'app collegata: flyout propria, mostrata dopo l'hook, non dentro
            Dispatcher.InvokeAsync(() => ShowFlyout(processName, displayName, appState));
            return true;
        }

        // App non utilizzabile: il tasto passa a Windows (volume o muto di sistema).
        // L'avviso (al massimo uno ogni 10 secondi, uguale per volume e muto) si mostra dopo l'hook.
        long now = Environment.TickCount64;
        if (_lastKeyNoticeTick < 0 || now - _lastKeyNoticeTick >= KeyNoticeIntervalMs)
        {
            _lastKeyNoticeTick = now;
            Dispatcher.InvokeAsync(() => ShowKeyNotice(processName, displayName));
        }

        return false;
    }

    // Flyout con nome, icona, livello e muto dell'app collegata
    private void ShowFlyout(string processName, string displayName, AppAudioState state)
    {
        try
        {
            _flyout ??= new VolumeFlyoutWindow();

            // L'icona si legge una sola volta per processo (anche se non è leggibile)
            if (!string.Equals(_flyoutIconFor, processName, StringComparison.OrdinalIgnoreCase))
            {
                _flyoutIcon = LoadProcessIcon(processName);
                _flyoutIconFor = processName;
            }

            _flyout.ShowFlyout(displayName, _flyoutIcon, (int)Math.Round(state.Volume * 100), state.IsMuted);
        }
        catch
        {
            // La flyout è solo un riscontro visivo: un errore non deve fermare il programma
        }
    }

    // Icona dell'eseguibile del processo; null se non leggibile (app dello Store, processi protetti)
    private static System.Windows.Media.ImageSource? LoadProcessIcon(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (path == null) continue;

                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    var source = CreateWindowIcon(icon);
                    if (source != null) return source;
                }
                catch
                {
                    // Accesso negato: si prova con il processo successivo
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        return null;
    }

    private void CheckSavedAppOnStartup()
    {
        var settings = SettingsService.Current;
        if (string.IsNullOrEmpty(settings.TargetProcessName)) return;

        bool isActive = AudioService.IsAppActive(settings.TargetProcessName);
        if (!isActive)
        {
            ShowAppNotFoundNotification(settings.TargetDisplayName ?? settings.TargetProcessName);
        }
    }

    // ✅ FIX 2 & 3: Sostituita tutta la logica Toast UWP con il balloon WinForms,
    // che funziona nativamente in WPF senza pacchetti aggiuntivi problematici.
    // Il click sul balloon apre la finestra impostazioni (registrato in OnStartup).
    private void ShowAppNotFoundNotification(string appName)
    {
        if (_trayIcon == null) return;

        // ShowBalloonTip è il metodo WinForms per le notifiche da tray icon.
        // - Primo parametro: durata minima suggerita in ms (Windows può ignorarla)
        // - BalloonTipTitle: titolo della notifica
        // - BalloonTipText: corpo del messaggio
        // - ToolTipIcon: icona della notifica (Info, Warning, Error, None)
        _trayIcon.BalloonTipTitle = "Volume Redirector";
        _trayIcon.BalloonTipText =
            $"L'applicazione \"{appName}\" non è in esecuzione.\n" +
            "Clicca qui per aprire le impostazioni e scegliere un'altra app.";
        _trayIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Warning;
        _trayIcon.ShowBalloonTip(5000); // 5 secondi
    }

    // Avviso mostrato quando un tasto volume non può essere gestito dall'app collegata.
    // Il clic sul balloon apre la finestra delle impostazioni (registrato in OnStartup).
    private void ShowKeyNotice(string processName, string displayName)
    {
        if (_trayIcon == null) return;

        // L'esistenza del processo si verifica solo ora, al momento di scrivere l'avviso
        if (IsProcessRunning(processName))
        {
            _trayIcon.BalloonTipText =
                $"{displayName} è aperta ma non sta riproducendo audio, uso il volume di sistema";
            _trayIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        }
        else
        {
            _trayIcon.BalloonTipText = $"{displayName} non è in esecuzione";
            _trayIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Warning;
        }

        _trayIcon.BalloonTipTitle = "Volume Redirector";
        _trayIcon.ShowBalloonTip(5000);
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.Show();
            _mainWindow.Activate();
            return;
        }

        // Finestra esistente: può essere nascosta nella tray o ridotta a icona
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        if (!_mainWindow.IsVisible)
            _mainWindow.Show();
        _mainWindow.Activate();
    }

    private System.Windows.Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Impostazioni", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => Shutdown());
        return menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _appIcon?.Dispose();
        _singleInstance?.Dispose();
        HotkeyService.Dispose();
        AudioService.Dispose();
        base.OnExit(e);
    }
}