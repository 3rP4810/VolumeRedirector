using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VolumeRedirector;

/// <summary>
/// Flyout nello stile di quella di Windows 11 (altoparlante, barra sottile, valore) con in più
/// icona e nome dell'app collegata, mostrata quando i tasti volume agiscono su di lei.
/// Non prende il focus e non è cliccabile.
/// </summary>
public partial class VolumeFlyoutWindow : Window
{
    // --- Costanti da ritoccare dopo una prova ---
    private const double BottomOffset = 60;   // px tra il bordo inferiore della scheda e la barra delle applicazioni
    private const int VisibleMs = 1500;       // permanenza dopo l'ultimo tasto
    private const int FadeInMs = 100;         // dissolvenza in entrata
    private const int FadeOutMs = 250;        // dissolvenza in uscita

    // --- Devono coincidere con il XAML ---
    private const double CardHeight = 64;     // Window.Height (92) - 2 * ShadowMargin
    private const double ShadowMargin = 14;   // Margin della scheda
    private const double BarWidth = 110;      // Width della barra

    private static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x67, 0xC0);
    private static readonly Color MutedBarColor = Color.FromRgb(0x8A, 0x8A, 0x8A);

    // Glifi di Segoe Fluent Icons: altoparlante senza onde, con 1, 2, 3 onde, e barrato
    private const string GlyphVolume0 = "\uE992";
    private const string GlyphVolume1 = "\uE993";
    private const string GlyphVolume2 = "\uE994";
    private const string GlyphVolume3 = "\uE995";
    private const string GlyphMute = "\uE74F";

    private readonly DispatcherTimer _hideTimer;
    private int _fadeVersion;
    private bool _fadingOut;
    private bool _light = true;
    private Color _accent = DefaultAccent;

    public VolumeFlyoutWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VisibleMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _fadingOut = true;
            FadeTo(0, FadeOutMs, () =>
            {
                _fadingOut = false;
                Hide();
            });
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Senza attivazione, fuori da Alt+Tab, i clic passano alla finestra sotto
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// Mostra (o aggiorna, se già visibile) la flyout e riavvia il conto della permanenza.
    /// </summary>
    public void ShowFlyout(string appName, ImageSource? appIcon, int percent, bool muted)
    {
        bool wasVisible = IsVisible;

        // Tema e colore di accento si rileggono solo a ogni nuova apparizione
        if (!wasVisible)
        {
            _light = IsSystemLightTheme();
            _accent = GetAccentColor();
        }

        percent = Math.Clamp(percent, 0, 100);
        ApplyContent(appName, appIcon, percent, muted);
        PositionOnPrimaryMonitor();

        _hideTimer.Stop();

        if (!wasVisible)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Show();
            FadeTo(1, FadeInMs, null);
        }
        else if (_fadingOut || Opacity < 1)
        {
            // Nuovo tasto durante la dissolvenza in uscita: torna visibile
            _fadingOut = false;
            FadeTo(1, FadeInMs, null);
        }

        _hideTimer.Start();
    }

    private void ApplyContent(string appName, ImageSource? appIcon, int percent, bool muted)
    {
        // Colori della flyout di sistema di Windows 11 (tema chiaro / scuro)
        Color background = _light ? Color.FromArgb(0xF5, 0xF9, 0xF9, 0xF9)
                                  : Color.FromArgb(0xF5, 0x2E, 0x2E, 0x2E);
        Color border = _light ? Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
                              : Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
        Color text = _light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;

        // Parte non riempita della barra; più tenue quando l'app è muta, per distinguerla dal riempimento grigio
        Color track = _light
            ? (muted ? Color.FromArgb(0x33, 0x00, 0x00, 0x00) : Color.FromArgb(0x72, 0x00, 0x00, 0x00))
            : (muted ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8B, 0xFF, 0xFF, 0xFF));

        Card.Background = new SolidColorBrush(background);
        Card.BorderBrush = new SolidColorBrush(border);

        var textBrush = new SolidColorBrush(text);
        NameText.Foreground = textBrush;
        ValueText.Foreground = textBrush;
        SpeakerIcon.Foreground = textBrush;
        AppIconFallback.Foreground = textBrush;

        BarTrack.Background = new SolidColorBrush(track);
        BarFill.Background = new SolidColorBrush(muted ? MutedBarColor : _accent);
        BarFill.Width = BarWidth * percent / 100.0;

        NameText.Text = appName;
        ValueText.Text = percent.ToString();
        ValueText.Opacity = muted ? 0.5 : 1.0;   // valore attenuato se muta

        // L'altoparlante c'è sempre: cambia con il livello ed è barrato se l'app è muta
        SpeakerIcon.Text = muted ? GlyphMute
                         : percent == 0 ? GlyphVolume0
                         : percent <= 33 ? GlyphVolume1
                         : percent <= 66 ? GlyphVolume2
                         : GlyphVolume3;

        if (appIcon != null)
        {
            AppIconImage.Source = appIcon;
            AppIconImage.Visibility = Visibility.Visible;
            AppIconFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Icona non leggibile (app dello Store, processi protetti): icona generica
            AppIconImage.Visibility = Visibility.Collapsed;
            AppIconFallback.Visibility = Visibility.Visible;
        }
    }

    private void PositionOnPrimaryMonitor()
    {
        // Area di lavoro del monitor principale (esclude la barra delle applicazioni)
        Rect work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Bottom - BottomOffset - CardHeight - ShadowMargin;
    }

    private void FadeTo(double target, int milliseconds, Action? completed)
    {
        int version = ++_fadeVersion;
        var animation = new DoubleAnimation(Opacity, target, TimeSpan.FromMilliseconds(milliseconds));
        animation.Completed += (_, _) =>
        {
            // Ignora il completamento di una dissolvenza già sostituita da un'altra
            if (version == _fadeVersion) completed?.Invoke();
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            // Tema di sistema (barra delle applicazioni e flyout), non quello delle app
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", null);
            return value is not int number || number != 0;
        }
        catch
        {
            return true;
        }
    }

    private Color GetAccentColor()
    {
        try
        {
            // Tavolozza dell'accento scelto in Impostazioni: 8 colori RGBA da 4 byte
            // (0 = più chiaro ... 3 = accento base, 4 = più scuro). Come i controlli di
            // Windows 11: nel tema scuro la barra usa la variante chiara (indice 1),
            // in quello chiaro la variante scura (indice 4).
            if (Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent",
                    "AccentPalette", null) is byte[] palette && palette.Length >= 20)
            {
                int offset = (_light ? 4 : 1) * 4;
                return Color.FromRgb(palette[offset], palette[offset + 1], palette[offset + 2]);
            }

            // Ripiego: colore di accento base, memorizzato come 0xAABBGGRR
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                                  "AccentColor", null) is int value)
            {
                return Color.FromRgb(
                    (byte)(value & 0xFF),
                    (byte)((value >> 8) & 0xFF),
                    (byte)((value >> 16) & 0xFF));
            }
        }
        catch
        {
            // Si usa il blu predefinito
        }

        return DefaultAccent;
    }

    // -------------------------
    // P/Invoke
    // -------------------------
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
