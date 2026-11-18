using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using RdpManager.Rdp;
using RdpManager.Services;
using RdpManager.ViewModels;
using RdpManager.Localization;

using Window = System.Windows.Window;

namespace RdpManager.Views;

public partial class FullScreenWindow : Window
{
    /// <summary>Imlec ust kenarin bu kadar yakinina gelince cubuk acilir.</summary>
    private const int HotZoneHeight = 3;

    /// <summary>Cubuk, imlec bu kadar asagi inince kapanir.</summary>
    private const int CloseMargin = 90;

    private readonly SessionTabItem _tab;
    private readonly SessionManager _manager;
    private readonly FullScreenBar _bar;
    private readonly DispatcherTimer _cursorWatch;

    private System.Drawing.Rectangle _monitorBounds;
    private DateTime _forcedVisibleUntil;

    public FullScreenWindow(SessionTabItem tab, SessionManager manager)
    {
        _tab = tab;
        _manager = manager;
        InitializeComponent();

        Title = Loc.F("{0} - tam ekran", tab.Title);

        // DIKKAT: Owner burada ATANMAZ. WPF, henuz gosterilmemis bir pencerenin
        // Owner olarak verilmesine izin vermiyor ("Owner ozelligi daha once
        // gosterilmemis bir Window'a ayarlanamaz"). Ilk Show'da atiyoruz.
        _bar = new FullScreenBar(tab, manager);

        // Fare RDP kontrolunde oldugu icin WPF fare olayi almaz;
        // imleci yoklayarak ust kenari yakaliyoruz.
        _cursorWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _cursorWatch.Tick += (_, _) => UpdateBarVisibility();

        // Pencere gorunur olunca Owner atanabilir; oncesinde WPF izin vermiyor.
        Loaded += (_, _) => _bar.Owner ??= this;
    }

    /// <summary>
    /// Host'u bu pencereye TAKAR. Yeni host yaratilmaz — oturum devam eder.
    /// </summary>
    public void AttachHost(ISessionHost host, Window? owner)
    {
        PositionOnMonitorOf(owner);
        HostSlot.Content = host;
    }

    public void DetachHost() => HostSlot.Content = null;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Cubugu birkac saniye gorunur tut ki kullanici nerede oldugunu ogrensin.
        _forcedVisibleUntil = DateTime.UtcNow.AddSeconds(3);
        ShowBar();
        _cursorWatch.Start();
    }

    /// <summary>
    /// Kaynak pencerenin bulundugu monitorde tam ekran yapar.
    ///
    /// Tek monitor: gomulu ActiveX, monitorlerin DPI'i farkliysa coklu monitor
    /// tam ekrani dogru cizemiyor. Tum monitorler icin cubuktaki mstsc secenegi var.
    /// </summary>
    private void PositionOnMonitorOf(Window? owner)
    {
        var handle = owner is not null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;

        var screen = handle != IntPtr.Zero
            ? Screen.FromHandle(handle)
            : Screen.PrimaryScreen ?? Screen.AllScreens[0];

        _monitorBounds = screen.Bounds;

        var dpi = NativeMethods.GetMonitorDpi(
            _monitorBounds.Left + _monitorBounds.Width / 2,
            _monitorBounds.Top + _monitorBounds.Height / 2);
        var scale = dpi / 96.0;

        // WindowState=Maximized + WindowStyle=None gorev cubugunu ortmez;
        // bu yuzden konumu elle veriyoruz.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _monitorBounds.Left / scale;
        Top = _monitorBounds.Top / scale;
        Width = _monitorBounds.Width / scale;
        Height = _monitorBounds.Height / scale;
    }

    // ------------------------------------------------------------ arac cubugu

    private void UpdateBarVisibility()
    {
        var (x, y) = NativeMethods.GetCursorPosition();

        var insideHorizontally = x >= _monitorBounds.Left && x <= _monitorBounds.Right;
        var atTopEdge = insideHorizontally && y <= _monitorBounds.Top + HotZoneHeight;

        if (atTopEdge)
        {
            ShowBar();
            return;
        }

        if (!_bar.IsVisible) return;
        if (DateTime.UtcNow < _forcedVisibleUntil) return;

        // Imlec cubuktan yeterince uzaklastiysa kapat.
        if (!insideHorizontally || y > _monitorBounds.Top + CloseMargin) _bar.Hide();
    }

    private void ShowBar()
    {
        if (!_bar.IsVisible)
        {
            // Owner ancak bu pencere gosterildikten sonra atanabilir.
            if (_bar.Owner is null && IsVisible) _bar.Owner = this;
            _bar.Show();
        }

        // Konumlandirma ancak olculdukten sonra dogru olur.
        Dispatcher.BeginInvoke(() =>
        {
            var dpi = NativeMethods.GetMonitorDpi(
                _monitorBounds.Left + _monitorBounds.Width / 2, _monitorBounds.Top + 1);
            var scale = dpi / 96.0;

            _bar.Left = (_monitorBounds.Left + _monitorBounds.Width / 2.0) / scale - _bar.ActualWidth / 2;
            _bar.Top = _monitorBounds.Top / scale;
        }, DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cursorWatch.Stop();
        _bar.Close();
        base.OnClosed(e);
    }
}
