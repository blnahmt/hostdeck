using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using AxMSTSCLib;
using MSTSCLib;
using RdpManager.Localization;
using RdpManager.Models;
using Serilog;
using UserControl = System.Windows.Controls.UserControl;

namespace RdpManager.Rdp;

/// <summary>
/// Tek bir RDP oturumunu barindiran WPF kontrolu.
///
/// ONEMLI: Bu kontrol asla yeniden yaratilmaz. Tam ekrana gecerken veya sekme
/// koparilirken sadece re-parent edilir (Faz 3). Yeniden yaratilirsa oturum kopar.
/// </summary>
public partial class RdpHost : UserControl, ISessionHost
{
    private static readonly ILogger Log = Serilog.Log.ForContext<RdpHost>();

    private RdpClientEx? _client;
    private RdpConnection _connection = new();
    private RdpSessionStatus _status = RdpSessionStatus.NotInitialized;
    private DispatcherTimer? _resizeDebounce;
    private bool _isDisposed;

    /// <summary>Sunucu saniyede birden fazla cozunurluk degisikligini reddediyor.</summary>
    private static readonly TimeSpan MinDisplayPushInterval = TimeSpan.FromSeconds(1);
    private DateTime _lastDisplayPushUtc = DateTime.MinValue;
    private (int Width, int Height) _lastPushedSize;

    /// <summary>Reconnect() bekliyor: kopma gelince yeniden baglan.</summary>
    private bool _reconnectAfterDisconnect;


    // --- Otomatik yeniden baglanma (yalnizca ag kopmalarinda) ---

    /// <summary>Deneme araliklari (saniye). Sonra pes edilir.</summary>
    private static readonly int[] RetryDelays = [5, 10, 20, 30, 30];

    private DispatcherTimer? _retryTimer;
    private int _retryAttempt;
    private int _secondsLeft;
    private bool _autoRetryDisabled;

    /// <summary>DPI/monitor degisimini dinledigimiz mevcut pencere.</summary>
    private Window? _dpiWindow;

    public RdpHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    // ---------------------------------------------------------------- durum

    public RdpSessionStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            Log.Debug("[{Conn}] durum -> {Status}", _connection.DisplayName, value);
            StatusChanged?.Invoke(this, value);
        }
    }

    public RdpConnection Connection => _connection;

    /// <summary>
    /// Baglanma sirasinda boyut degistirmek ActiveX kontrolunu kilitliyor.
    /// Sekme/pencere yoneticisi resize etmeden once bunu sormali.
    /// </summary>
    public bool CanResizeNow => Status is RdpSessionStatus.Connected or RdpSessionStatus.Disconnected;

    public event EventHandler<RdpSessionStatus>? StatusChanged;
    public event EventHandler? Connected;
    public event EventHandler<RdpDisconnectInfo>? Disconnected;

    // ------------------------------------------------------------- baglanti

    public void Initialize(RdpConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Sekme <-> tam ekran gecislerinde de tetiklenir: DPI izlemesini
        // her yuklemede (yeni pencereye) yeniden bagla.
        AttachDpiWatch();

        // Handle olusmadan ActiveX yaratilamaz; bu yuzden ctor'da degil burada.
        if (_client is not null || _isDisposed) return;
        if (string.IsNullOrWhiteSpace(_connection.Address)) return;

        CreateClient();
        Connect();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachDpiWatch();

    /// <summary>
    /// Barindiran pencerenin DPI/monitor degisimini dinler. Farkli olcek/DPI'li
    /// bir monitore tasininca cozunurlugu yeniden gonderir; aksi halde goruntu
    /// eski cozunurlukte gerilir (bulanik gorunur).
    /// </summary>
    private void AttachDpiWatch()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _dpiWindow)) return;

        DetachDpiWatch();
        _dpiWindow = window;
        if (_dpiWindow is not null) _dpiWindow.DpiChanged += OnHostDpiChanged;
    }

    private void DetachDpiWatch()
    {
        if (_dpiWindow is null) return;
        _dpiWindow.DpiChanged -= OnHostDpiChanged;
        _dpiWindow = null;
    }

    private void OnHostDpiChanged(object? sender, System.Windows.DpiChangedEventArgs e)
    {
        if (Status != RdpSessionStatus.Connected) return;

        // Boyut degismeden yalniz DPI degisebilir; cozunurlugu debounce ile yeniden gonder.
        _resizeDebounce ??= CreateDebounceTimer();
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void CreateClient()
    {
        _client = new RdpClientEx
        {
            Dock = DockStyle.Fill,
            // Kontrol henuz cizim yapmadigi anlarda beyaz yerine koyu zemin.
            BackColor = System.Drawing.Color.FromArgb(0x14, 0x14, 0x16),
        };
        FormsHost.Child = _client;

        // ActiveX'i simdi olustur — ozellikler ancak bundan sonra set edilebilir.
        _client.CreateControl();

        _client.OnConnecting += (_, _) => ShowOverlay(Loc.F("{0} baglaniyor...", _connection.DisplayName), showReconnect: false);
        _client.OnConnected += (_, _) => HandleConnected();
        _client.OnDisconnected += (_, args) => HandleDisconnected(args.discReason);
        _client.OnFatalError += (_, args) => Log.Error("[{Conn}] fatal error {Code}", _connection.DisplayName, args.errorCode);
    }

    public void Connect()
    {
        if (_client is null || _isDisposed) return;
        if (Status is RdpSessionStatus.Connecting or RdpSessionStatus.Connected) return;

        try
        {
            ApplySettings();
            Status = RdpSessionStatus.Connecting;
            ShowOverlay(Loc.F("{0} baglaniyor...", _connection.DisplayName), showReconnect: false);
            _client.Connect();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Conn}] baglanti baslatilamadi", _connection.DisplayName);
            Status = RdpSessionStatus.Disconnected;
            ShowOverlay(Loc.F("Baglanti baslatilamadi: {0}", ex.Message), showReconnect: true);
        }
    }

    /// <summary>
    /// Uzak oturuma Ctrl+Alt+Del gonderir.
    ///
    /// Kontrolun kendi SendKeys'i kullaniliyor: tuslar dogrudan oturuma gider,
    /// yerel klavye odagindan bagimsizdir. (Arac cubugu butonuna basildiginda
    /// odak butona gectigi icin keybd_event ile enjeksiyon calismiyordu.)
    /// </summary>
    public void SendCtrlAltDelete()
    {
        if (_client is null || Status != RdpSessionStatus.Connected) return;

        // Scan kodlari: Ctrl=29, Alt=56, Del=83. Once bas, sonra ters sirada birak.
        var keyUp = new[] { false, false, false, true, true, true };
        var keyData = new[] { 29, 56, 83, 83, 56, 29 };

        try
        {
            if (_client.GetOcx() is IMsRdpClientNonScriptable nonScriptable)
            {
                nonScriptable.SendKeys(keyUp.Length, ref keyUp[0], ref keyData[0]);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SendKeys basarisiz, klavye enjeksiyonuna dusuluyor");
        }

        // Yedek yol: kontrole odaklan ve Ctrl+Alt+End gonder (RDP bunu
        // oturum icinde Ctrl+Alt+Del olarak yorumlar).
        FormsHost.Focus();
        _client.Focus();
        Dispatcher.BeginInvoke(NativeMethods.SendCtrlAltEnd, DispatcherPriority.Input);
    }

    /// <summary>
    /// Oturumu kapatip yeniden acar.
    ///
    /// Connect() bagliyken erken donduğu icin dogrudan cagirmak ise yaramiyordu;
    /// once baglantiyi kesip kontrolun kapanmasini beklemek gerekiyor.
    /// </summary>
    public void Reconnect()
    {
        if (_client is null || _isDisposed) return;

        if (Status is RdpSessionStatus.Connected or RdpSessionStatus.Connecting)
        {
            // OnDisconnected geldiginde durum Disconnected olur; sonra baglaniriz.
            _reconnectAfterDisconnect = true;
            Disconnect();
            return;
        }

        Connect();
    }

    /// <summary>
    /// Oturumun mevcut goruntusunu yakalayip kucuk resim olarak kaydeder.
    /// Kapatmadan/kesmeden ONCE, hala bagli ve gorunurken cagrilmali.
    /// Basari durumunu doner (bos/yakalanamayan goruntu kaydedilmez).
    /// </summary>
    public bool CaptureThumbnail()
    {
        if (_client is null || _isDisposed) return false;
        if (Status != RdpSessionStatus.Connected) return false;
        // Yalniz gercekten ekranda gorunen (secili, on plandaki) oturumu yakala;
        // aksi halde ekrandan kopyalama baska sekmenin goruntusunu alir.
        if (!IsVisible || FormsHost.Visibility != Visibility.Visible) return false;

        try
        {
            var size = _client.ClientSize;
            if (size.Width < 2 || size.Height < 2) return false;

            var origin = _client.PointToScreen(System.Drawing.Point.Empty);

            using var bitmap = new System.Drawing.Bitmap(size.Width, size.Height);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(origin, System.Drawing.Point.Empty, size);
            }

            return Services.ThumbnailStore.Save(bitmap, _connection.Id);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[{Conn}] kucuk resim yakalanamadi", _connection.DisplayName);
            return false;
        }
    }

    public void Disconnect()
    {
        if (_client is null) return;
        try
        {
            if (_client.Connected != 0) _client.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Conn}] disconnect sirasinda hata", _connection.DisplayName);
        }
    }

    private void ApplySettings()
    {
        if (_client is null) return;
        var c = _connection;

        _client.Server = c.Address;
        _client.UserName = c.UserName;
        _client.Domain = c.Domain;

        _client.ColorDepth = c.ColorDepth;

        var adv = _client.AdvancedSettings9;
        adv.RDPPort = c.Port;
        adv.ClearTextPassword = c.Password ?? string.Empty;
        adv.EnableCredSspSupport = c.EnableCredSsp;
        adv.AuthenticationLevel = (uint)c.AuthenticationLevel;
        adv.SmartSizing = c.SmartSizing;
        adv.DisplayConnectionBar = c.DisplayConnectionBar;
        adv.EnableAutoReconnect = c.EnableAutoReconnect;
        adv.RedirectDrives = c.RedirectDrives;
        adv.RedirectPrinters = c.RedirectPrinters;
        adv.RedirectPorts = c.RedirectPorts;
        adv.RedirectSmartCards = c.RedirectSmartCards;
        adv.RedirectClipboard = c.RedirectClipboard;
        adv.AudioRedirectionMode = (uint)c.AudioRedirectionMode;
        adv.GrabFocusOnConnect = true;

        ApplyKeyboardCapture(c.SendWindowsKeys);
        ApplyCameraRedirection(c.RedirectCameras);
        ApplyGateway(c.GatewayHostname);

        var (width, height) = GetPixelSize();
        width = Math.Clamp(width & ~1, 200, 8192);
        height = Math.Clamp(height & ~1, 200, 8192);

        _client.DesktopWidth = width;
        _client.DesktopHeight = height;

        // Baglanti bu cozunurlukla kuruluyor; resize karsilastirmasinin baslangici bu.
        _lastPushedSize = (width, height);

        ApplyDpi();
    }

    /// <summary>
    /// Windows tusu ve Win+Shift+S gibi kisayollarin nereye gidecegini belirler.
    ///
    /// KeyboardHookMode:
    ///   0 = yerel bilgisayara uygula
    ///   1 = her zaman uzak oturuma uygula
    ///   2 = yalniz tam ekranda uzak oturuma uygula (mstsc varsayilani)
    ///
    /// Varsayilan 2 oldugu icin pencere modunda Windows tusu yerel Baslat
    /// menusunu aciyordu.
    /// </summary>
    private void ApplyKeyboardCapture(bool sendToRemote)
    {
        if (_client is null) return;

        try
        {
            _client.AdvancedSettings9.EnableWindowsKey = 1;
            _client.SecuredSettings3.KeyboardHookMode = sendToRemote ? 1 : 2;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Klavye yakalama ayarlanamadi");
        }
    }

    /// <summary>
    /// RD Gateway. Gateway, RDP ile ayni kullanici adi/parolayi kullanir
    /// (mstsc'deki "RD Gateway icin ayni kimlik bilgilerini kullan" secenegi).
    /// </summary>
    private void ApplyGateway(string? gateway)
    {
        if (_client is null || string.IsNullOrWhiteSpace(gateway)) return;

        try
        {
            var t = _client.TransportSettings2;
            t.GatewayHostname = gateway.Trim();
            t.GatewayUsageMethod = 1;        // TSC_PROXY_MODE_DIRECT: her zaman gateway uzerinden
            t.GatewayProfileUsageMethod = 1; // yukaridaki acik ayarlari kullan
            t.GatewayCredsSource = 0;        // kullanici adi + parola
            t.GatewayCredSharing = 1;        // RDP kimlik bilgilerini gateway'e de ver
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Conn}] RD Gateway ayarlanamadi", _connection.DisplayName);
        }
    }

    /// <summary>
    /// Kamera yonlendirmesi. Ayri bir arayuz uzerinden yapiliyor
    /// (AdvancedSettings'te karsiligi yok) ve sunucu tarafinda
    /// Windows 10 1803 / Server 2019 ve sonrasi gerekiyor.
    /// </summary>
    private void ApplyCameraRedirection(bool enabled)
    {
        if (_client is null) return;

        try
        {
            if (_client.GetOcx() is not IMsRdpClientNonScriptable7 nonScriptable) return;

            // RedirectByDefault = tum kameralar; tek tek secim ileride eklenebilir.
            nonScriptable.CameraRedirConfigCollection.RedirectByDefault = enabled;
        }
        catch (Exception ex)
        {
            // Eski mstscax surumlerinde bu arayuz yok - olumcul degil.
            Log.Debug(ex, "Kamera yonlendirmesi uygulanamadi (istenen: {Enabled})", enabled);
        }
    }

    /// <summary>
    /// HiDPI ayarlari MUTLAKA Connect() oncesinde set edilmeli — sonra etkisiz.
    /// </summary>
    private void ApplyDpi()
    {
        if (_client is null) return;

        var (desktopScale, deviceScale) = GetScaleFactors();

        try
        {
            if (_client.GetOcx() is not IMsRdpExtendedSettings ext) return;
            object desktopValue = desktopScale;
            object deviceValue = deviceScale;
            ext.set_Property("DesktopScaleFactor", ref desktopValue);
            ext.set_Property("DeviceScaleFactor", ref deviceValue);
        }
        catch (Exception ex)
        {
            // Eski mstscax surumlerinde IMsRdpExtendedSettings yok — olumcul degil.
            Log.Warning(ex, "DPI ayarlari uygulanamadi (DesktopScaleFactor={Scale})", desktopScale);
        }
    }

    /// <summary>
    /// DesktopScaleFactor 100-500 arasi serbest; DeviceScaleFactor SADECE
    /// 100 / 140 / 180 kabul ediyor, baska deger verilirse cagri reddediliyor.
    /// </summary>
    private (uint DesktopScale, uint DeviceScale) GetScaleFactors()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var desktopScale = _connection.DesktopScaleFactor > 0
            ? (uint)Math.Clamp(_connection.DesktopScaleFactor, 100, 500)
            : (uint)Math.Clamp(Math.Round(dpi.DpiScaleX * 100), 100, 500);

        uint deviceScale = desktopScale switch
        {
            >= 180 => 180,
            >= 140 => 140,
            _ => 100
        };

        return (desktopScale, deviceScale);
    }

    // ---------------------------------------------------------------- olaylar

    private void HandleConnected()
    {
        Status = RdpSessionStatus.Connected;

        // Basarili baglanti deneme sayacini sifirlar: sonraki kopmada
        // yeniden bes deneme hakki olur.
        _retryAttempt = 0;
        StopRetryCountdown();
        HideOverlay();

        Log.Information("[{Conn}] baglandi", _connection.DisplayName);

        // Baglanma suresince pencere yeniden boyutlandirilmis olabilir.
        Dispatcher.BeginInvoke(PushDisplaySettings);

        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void HandleDisconnected(int reason)
    {
        var extended = 0;
        try { extended = (int)(_client?.ExtendedDisconnectReason ?? 0); }
        catch { /* kontrol zaten yok olmus olabilir */ }

        var info = new RdpDisconnectInfo(
            reason,
            extended,
            Describe(reason, extended),
            RdpDisconnectReasons.IsUserInitiated(reason),
            RdpDisconnectReasons.IsNetworkFailure(reason));

        Status = RdpSessionStatus.Disconnected;
        Log.Information("[{Conn}] baglanti kesildi: {Info}", _connection.DisplayName, info);

        if (_reconnectAfterDisconnect)
        {
            _reconnectAfterDisconnect = false;
            ShowOverlay(Loc.F("{0} yeniden baglaniyor...", _connection.DisplayName), showReconnect: false);

            // Kontrolun kopma islemini bitirmesini bekle, sonra yeniden bagla.
            Dispatcher.BeginInvoke(Connect, DispatcherPriority.Background);
            Disconnected?.Invoke(this, info);
            return;
        }

        // Yalniz ag/soket kopmalarinda otomatik dene. Kullanici veya sunucu
        // bilerek kapattiysa (baska biri baglandi, oturum kapatildi) denenmez.
        if (info.IsNetworkFailure && !_autoRetryDisabled && _retryAttempt < RetryDelays.Length)
        {
            StartRetryCountdown(info.Message);
            Disconnected?.Invoke(this, info);
            return;
        }

        ShowOverlay(info.Message, showReconnect: true);
        Disconnected?.Invoke(this, info);
    }

    private string Describe(int reason, int extended)
    {
        if (RdpDisconnectReasons.TryDescribe(reason, out var known))
            return known;

        // Bilinmeyen kodlar icin otoriter kaynak kontrolun kendisi:
        // Windows tarafindan yerellestirilmis aciklamayi dondurur.
        try
        {
            var description = _client?.GetErrorDescription((uint)reason, (uint)extended);
            if (!string.IsNullOrWhiteSpace(description)) return description;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "GetErrorDescription basarisiz ({Reason}/{Extended})", reason, extended);
        }

        return Loc.F("Baglanti kesildi (kod {0}).", reason);
    }

    // ---------------------------------------------------------------- resize

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Status != RdpSessionStatus.Connected) return;

        // SmartSizing acik olsa bile cozunurlugu gercekten degistiriyoruz.
        // Aksi halde uzak masaustu sabit cozunurlukta kalip gerilir (bulanik gorunum);
        // SmartSizing sadece surukleme sirasindaki ara kareler icin devrede.
        _resizeDebounce ??= CreateDebounceTimer();
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private DispatcherTimer CreateDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            PushDisplaySettings();
        };
        return timer;
    }

    /// <summary>
    /// Uzak oturumun cozunurlugunu pencerenin gercek boyutuna esitler.
    /// Basarili olursa goruntu gerilmez, sunucu yeni cozunurlukte cizer.
    /// </summary>
    private void PushDisplaySettings()
    {
        if (_client is null || Status != RdpSessionStatus.Connected) return;

        var (width, height) = GetPixelSize();

        // RDP cift sayili genislik/yukseklik istiyor, sinirlar 200-8192.
        width = Math.Clamp(width & ~1, 200, 8192);
        height = Math.Clamp(height & ~1, 200, 8192);

        if ((width, height) == _lastPushedSize) return;

        // Cok sik gonderirsek sunucu reddediyor; biraz sonra tekrar dene.
        if (DateTime.UtcNow - _lastDisplayPushUtc < MinDisplayPushInterval)
        {
            _resizeDebounce?.Start();
            return;
        }

        var (desktopScale, deviceScale) = GetScaleFactors();

        try
        {
            _client.UpdateSessionDisplaySettings(
                (uint)width, (uint)height,
                0, 0,   // fiziksel boyut (mm) — belirtilmiyor
                0,      // yon: yatay
                desktopScale, deviceScale);

            _lastPushedSize = (width, height);
            _lastDisplayPushUtc = DateTime.UtcNow;
            Log.Debug("[{Conn}] cozunurluk -> {Width}x{Height} @%{Scale}",
                _connection.DisplayName, width, height, desktopScale);
        }
        catch (Exception ex)
        {
            // Sunucu Display Control DVC desteklemiyorsa (Win7/2008) normal:
            // bu durumda SmartSizing devrede kalir ve goruntu gerilir.
            Log.Debug(ex, "UpdateSessionDisplaySettings desteklenmiyor - SmartSizing'e birakiliyor");
        }
    }

    private (int Width, int Height) GetPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Math.Max(ActualWidth, 800) * dpi.DpiScaleX);
        var height = (int)Math.Round(Math.Max(ActualHeight, 600) * dpi.DpiScaleY);
        return (width, height);
    }

    // --------------------------------------------------------------- overlay

    /// <summary>
    /// DIKKAT (airspace): WPF ogeleri WindowsFormsHost'un (bir HWND) UZERINE
    /// cizilemez. Katman gorunur olsun diye ActiveX'i GIZLEMEK zorundayiz —
    /// aksi halde "Baglaniyor..." ve hata mesajlari hicbir zaman gorunmez,
    /// kullanici sadece kontrolun bos beyaz yuzeyini gorur.
    /// </summary>
    private void ShowOverlay(string message, bool showReconnect)
    {
        Dispatcher.Invoke(() =>
        {
            OverlayText.Text = message;
            ReconnectButton.Visibility = showReconnect ? Visibility.Visible : Visibility.Collapsed;

            // Yalniz baglanma sirasinda ilerleme cizgisi goster.
            ConnectingBar.Visibility = Status == RdpSessionStatus.Connecting
                ? Visibility.Visible
                : Visibility.Collapsed;

            OverlayPanel.Visibility = Visibility.Visible;

            // Hidden (Collapsed degil): kontrol yerleseni koruyor, boyutu bozulmuyor.
            FormsHost.Visibility = Visibility.Hidden;
        });
    }

    private void HideOverlay()
        => Dispatcher.Invoke(() =>
        {
            OverlayPanel.Visibility = Visibility.Collapsed;
            FormsHost.Visibility = Visibility.Visible;
        });

    private void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        StopRetryCountdown();
        Connect();
    }

    private void CancelRetryButton_Click(object sender, RoutedEventArgs e)
    {
        _autoRetryDisabled = true;
        StopRetryCountdown();
        ShowOverlay(Loc.T("Otomatik yeniden baglanma durduruldu."), showReconnect: true);
    }

    // ------------------------------------------- otomatik yeniden baglanma

    private void StartRetryCountdown(string reason)
    {
        _secondsLeft = RetryDelays[_retryAttempt];
        _retryAttempt++;

        Dispatcher.Invoke(() =>
        {
            OverlayText.Text = reason;
            ReconnectButton.Content = Loc.T("Simdi dene");
            ReconnectButton.Visibility = Visibility.Visible;
            CancelRetryButton.Visibility = Visibility.Visible;
            CountdownText.Visibility = Visibility.Visible;
            OverlayPanel.Visibility = Visibility.Visible;
            FormsHost.Visibility = Visibility.Hidden;
            UpdateCountdownText();
        });

        _retryTimer ??= CreateRetryTimer();
        _retryTimer.Start();

        Log.Information("[{Conn}] ag kopmasi, {Seconds} sn sonra {Attempt}. deneme",
            _connection.DisplayName, _secondsLeft, _retryAttempt);
    }

    private DispatcherTimer CreateRetryTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft > 0)
            {
                UpdateCountdownText();
                return;
            }

            StopRetryCountdown();
            Connect();
        };
        return timer;
    }

    private void UpdateCountdownText()
        => CountdownText.Text = Loc.F("{0} sn sonra yeniden denenecek ({1}/{2})",
                              _secondsLeft, _retryAttempt, RetryDelays.Length);

    private void StopRetryCountdown()
    {
        _retryTimer?.Stop();
        Dispatcher.Invoke(() =>
        {
            CountdownText.Visibility = Visibility.Collapsed;
            CancelRetryButton.Visibility = Visibility.Collapsed;
            ReconnectButton.Content = Loc.T("Yeniden baglan");
        });
    }

    // --------------------------------------------------------------- dispose

    /// <summary>
    /// DIKKAT: Bunu oturum kapanir kapanmaz cagirma. ActiveX kontrolu kendi
    /// mesajlarini isliyorken dispose edilirse uygulama cokuyor.
    /// <see cref="Services.HostDisposeQueue"/> uzerinden geciktirerek cagir.
    /// </summary>
    public void DisposeHost()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _resizeDebounce?.Stop();
        _resizeDebounce = null;
        _retryTimer?.Stop();
        _retryTimer = null;

        try
        {
            if (_client is not null)
            {
                if (_client.Connected != 0) _client.Disconnect();
                FormsHost.Child = null;
                _client.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Conn}] host dispose edilirken hata", _connection.DisplayName);
        }
        finally
        {
            _client = null;
        }
    }
}
