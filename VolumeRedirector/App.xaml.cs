using System.Diagnostics;
using System.Windows;
using VolumeRedirector.Models;
using VolumeRedirector.Services;

namespace VolumeRedirector;

public partial class App : Application
{
    public static HotkeyService HotkeyService { get; } = new();
    public static AudioService AudioService { get; } = new();
    public static SettingsService SettingsService { get; } = new();
    public static StartupShortcutService StartupShortcutService { get; } = new();
    public static BluetoothVolumeService BluetoothVolumeService { get; } = new();
    public static ThemeService ThemeService { get; } = new();

    // Intervallo minimo tra due avvisi mostrati alla pressione dei tasti volume
    // (condiviso con i gesti Bluetooth)
    private const long KeyNoticeIntervalMs = 10_000;

    /// <summary>Icona per le finestre (null se non disponibile).</summary>
    public static System.Windows.Media.ImageSource? WindowIcon { get; private set; }

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private System.Drawing.Icon? _appIcon;
    private SingleInstanceService? _singleInstance;
    private long _lastKeyNoticeTick = -1;

    // Flyout dell'app collegata (creata alla prima occorrenza)
    private VolumeFlyoutWindow? _flyout;

    // Icone dei processi in cache (anche quelle non leggibili, che restano null): usata da flyout ed elenco app
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> ProcessIconCache =
        new(StringComparer.OrdinalIgnoreCase);

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

        // Log locale e gestione globale degli errori non gestiti (solo nell'istanza principale)
        LogService.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogService.Error("Errore non gestito", args.ExceptionObject as Exception);
            LogService.Flush();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("Errore non gestito nell'interfaccia", args.Exception);
            LogService.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
            LogService.Error("Errore non gestito in un'attività", args.Exception);
        LogService.Info(runMode ? "Avvio (--run)" : "Avvio (manuale)");

        base.OnStartup(e);

        // Risorse di WPF UI già caricate da App.xaml: qui si applica il tema salvato
        ThemeService.Apply(SettingsService.Current.Theme);

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

        // Gesti delle cuffie Bluetooth (variazioni del volume del dispositivo di uscita).
        // In avvio automatico (--run) non compare nessun avviso, nemmeno sul livello del volume.
        BluetoothVolumeService.ShouldAct = ShouldInterceptBluetooth;
        BluetoothVolumeService.GestureHandler = OnBluetoothGesture;
        BluetoothVolumeService.EdgeLevelHandler = ShowBluetoothLevelNotice;
        BluetoothVolumeService.Start(Dispatcher, checkLevelAtStart: !runMode);

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
            LogService.Info($"Tasto {KeyName(key)} gestito su {processName}");
            Dispatcher.InvokeAsync(() => ShowFlyout(processName, displayName, appState));
            return true;
        }

        // App non utilizzabile: il tasto passa a Windows (volume o muto di sistema).
        // L'avviso (al massimo uno ogni 10 secondi, uguale per volume e muto) si mostra dopo l'hook.
        LogService.Info($"Tasto {KeyName(key)} passato a Windows ({processName} non utilizzabile)");
        if (TryTakeNoticeSlot())
            Dispatcher.InvokeAsync(() => ShowKeyNotice(processName, displayName));

        return false;
    }

    // Chiamato (thread UI) a ogni gesto Volume Su / Giù delle cuffie Bluetooth.
    // Restituisce true se il gesto è stato gestito sull'app collegata (il volume di sistema
    // viene allora rimesso a posto dal servizio), false se resta il cambio del volume di sistema.
    private bool OnBluetoothGesture(VolumeDirection direction)
    {
        var settings = SettingsService.Current;
        string processName = settings.TargetProcessName ?? string.Empty;
        if (processName.Length == 0) return false;

        string displayName = string.IsNullOrWhiteSpace(settings.TargetDisplayName)
            ? processName
            : settings.TargetDisplayName;

        string name = direction == VolumeDirection.Up ? "Volume Su" : "Volume Giù";
        float delta = direction == VolumeDirection.Up ? settings.VolumeStep : -settings.VolumeStep;

        if (AudioService.AdjustVolume(processName, delta) is { } appState)
        {
            LogService.Info($"Gesto Bluetooth {name} gestito su {processName}");
            ShowFlyout(processName, displayName, appState);
            return true;
        }

        // App non utilizzabile: il volume di sistema resta com'è dopo il gesto, con lo stesso
        // avviso (e lo stesso limite di 10 secondi) dei tasti
        LogService.Info($"Gesto Bluetooth {name} passato a Windows ({processName} non utilizzabile)");
        if (TryTakeNoticeSlot())
            ShowKeyNotice(processName, displayName);

        return false;
    }

    // I gesti si gestiscono solo con interruttore acceso e app salvata
    private static bool ShouldInterceptBluetooth()
    {
        var settings = SettingsService.Current;
        return settings.InterceptBluetooth && !string.IsNullOrEmpty(settings.TargetProcessName);
    }

    // True se è passato abbastanza tempo dall'ultimo avviso di tasto o gesto (e prenota il posto)
    private bool TryTakeNoticeSlot()
    {
        long now = Environment.TickCount64;
        if (_lastKeyNoticeTick >= 0 && now - _lastKeyNoticeTick < KeyNoticeIntervalMs)
            return false;

        _lastKeyNoticeTick = now;
        return true;
    }

    private static string KeyName(VolumeKey key) => key switch
    {
        VolumeKey.Up => "Volume Su",
        VolumeKey.Down => "Volume Giù",
        _ => "Muto"
    };

    // Flyout con nome, icona, livello e muto dell'app collegata
    private void ShowFlyout(string processName, string displayName, AppAudioState state)
    {
        try
        {
            _flyout ??= new VolumeFlyoutWindow();

            _flyout.ShowFlyout(displayName, GetProcessIcon(processName),
                               (int)Math.Round(state.Volume * 100), state.IsMuted);
        }
        catch (Exception ex)
        {
            // La flyout è solo un riscontro visivo: un errore non deve fermare il programma
            LogService.Error("Errore nella flyout", ex);
        }
    }

    /// <summary>
    /// Icona del processo, letta una sola volta per processo (anche se non è leggibile: allora
    /// resta null e chi la mostra usa un'icona generica). Da usare sul thread UI.
    /// </summary>
    public static System.Windows.Media.ImageSource? GetProcessIcon(string processName)
    {
        if (!ProcessIconCache.TryGetValue(processName, out var icon))
        {
            icon = LoadProcessIcon(processName);
            ProcessIconCache[processName] = icon;
        }

        return icon;
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
        LogService.Info($"Avviso mostrato: {appName} non è in esecuzione (avvio)");
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
        LogService.Info($"Avviso mostrato: {_trayIcon.BalloonTipText}");
    }

    // Avviso mostrato (una volta per connessione) quando il volume di sistema di un'uscita Bluetooth
    // è così alto o basso che i gesti delle cuffie non producono nessuna variazione
    private void ShowBluetoothLevelNotice(int percent)
    {
        if (_trayIcon == null) return;

        _trayIcon.BalloonTipTitle = "Volume Redirector";
        _trayIcon.BalloonTipText =
            $"Volume di sistema al {percent}%: il Volume Su/Giù delle cuffie non viene rilevato. Portalo a metà.";
        _trayIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Warning;
        _trayIcon.ShowBalloonTip(5000);
        LogService.Info($"Avviso mostrato: {_trayIcon.BalloonTipText}");
    }

    internal static bool IsProcessRunning(string processName)
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
        BluetoothVolumeService.Dispose();
        AudioService.Dispose();
        LogService.Info("Uscita");
        LogService.Shutdown();
        base.OnExit(e);
    }
}