using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.Input;

namespace RdpManager.Views;

/// <summary>
/// Windows'un baslik cubugu yerine uygulamanin kendi cubugunu kullanan pencere.
///
/// Suruklemeyi WPF'te elle yapmiyoruz: WindowChrome.CaptionHeight ile ust seridi
/// Windows'a "baslik cubugu" olarak bildiriyoruz. Boylece buyutulmus pencereyi
/// surukleyince eski boyutuna donme, cift tikla buyutme ve Aero Snap
/// isletim sisteminin kendi davranisiyla calisiyor.
///
/// Seritteki tiklanabilir ogeler WindowChrome.IsHitTestVisibleInChrome="True"
/// ile isaretlenmeli, aksi halde tiklamalari Windows yutar.
/// </summary>
public class ChromeWindow : Window
{
    static ChromeWindow()
    {
        // Stil Themes/Generic.xaml'de. App.xaml'de implicit style ise yaramaz:
        // implicit style yalnizca TAM tipe uygulanir, MainWindow gibi turetilmis
        // pencerelere miras kalmaz. DefaultStyleKey tema sozlugunden cozer.
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(typeof(ChromeWindow)));
    }

    public ChromeWindow()
    {
        // WindowStyle pencere handle'i olustuktan sonra degistirilemez ve
        // Style setter'i gec kalabiliyor; yapicida vermek tek guvenilir yol.
        WindowStyle = WindowStyle.None;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute));

        MinimizeCommand = new RelayCommand(() => WindowState = WindowState.Minimized);
        MaximizeRestoreCommand = new RelayCommand(ToggleMaximize);
        CloseWindowCommand = new RelayCommand(Close);
    }

    /// <summary>Seridin ortasina/soluna yerlestirilecek ek icerik.</summary>
    public static readonly DependencyProperty TitleBarContentProperty =
        DependencyProperty.Register(nameof(TitleBarContent), typeof(object), typeof(ChromeWindow));

    /// <summary>Pencere dugmelerinin hemen soluna gelen eylemler.</summary>
    public static readonly DependencyProperty TitleBarActionsProperty =
        DependencyProperty.Register(nameof(TitleBarActions), typeof(object), typeof(ChromeWindow));

    /// <summary>
    /// false ise ayri bir baslik seridi cizilmez; pencere dugmeleri
    /// pencerenin kendi icerigi (ornegin sekme seridi) uzerine biner.
    /// Boylece tek satirlik bir cubuk elde edilir.
    /// </summary>
    public static readonly DependencyProperty ShowTitleBarProperty =
        DependencyProperty.Register(nameof(ShowTitleBar), typeof(bool), typeof(ChromeWindow),
            new PropertyMetadata(true));

    /// <summary>Seridin yuksekligi. Windows'a bildirilen caption yuksekligi de budur.</summary>
    public static readonly DependencyProperty TitleBarHeightProperty =
        DependencyProperty.Register(nameof(TitleBarHeight), typeof(double), typeof(ChromeWindow),
            new PropertyMetadata(34.0));

    public static readonly DependencyProperty ShowMaximizeButtonProperty =
        DependencyProperty.Register(nameof(ShowMaximizeButton), typeof(bool), typeof(ChromeWindow),
            new PropertyMetadata(true));

    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    public object? TitleBarActions
    {
        get => GetValue(TitleBarActionsProperty);
        set => SetValue(TitleBarActionsProperty, value);
    }

    public bool ShowTitleBar
    {
        get => (bool)GetValue(ShowTitleBarProperty);
        set => SetValue(ShowTitleBarProperty, value);
    }

    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty);
        set => SetValue(TitleBarHeightProperty, value);
    }

    public bool ShowMaximizeButton
    {
        get => (bool)GetValue(ShowMaximizeButtonProperty);
        set => SetValue(ShowMaximizeButtonProperty, value);
    }

    public ICommand MinimizeCommand { get; }

    public ICommand MaximizeRestoreCommand { get; }

    public ICommand CloseWindowCommand { get; }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        // XAML'deki TitleBarHeight okunduktan sonra, pencere gosterilmeden once.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TitleBarHeight,        // bu serit Windows icin baslik cubugu
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0), // DWM golgesi/cam cercevesi kapali
            CornerRadius = default,
            UseAeroCaptionButtons = false,
        });
    }

    private bool _fadedIn;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Acilista tek seferlik yumusak fade (tema degisiminde tekrarlamaz).
        if (!_fadedIn && GetTemplateChild("WindowRoot") is FrameworkElement root)
        {
            _fadedIn = true;
            root.Opacity = 0;
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Buyutulunce pencerenin monitorden tasmasini engelle.
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != ChromeInterop.WM_GETMINMAXINFO) return IntPtr.Zero;

        ChromeInterop.ClampMaximizedToWorkArea(hwnd, lParam);
        handled = true;
        return IntPtr.Zero;
    }

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
