using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarcusW.VncClient;
using MarcusW.VncClient.Protocol.Implementation.MessageTypes.Outgoing;
using MarcusW.VncClient.Protocol.Implementation.Services.Transports;
using MarcusW.VncClient.Protocol.SecurityTypes;
using MarcusW.VncClient.Security;
using Microsoft.Extensions.Logging.Abstractions;
using RdpManager.Localization;
using RdpManager.Models;
using Serilog;

using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtons = MarcusW.VncClient.MouseButtons;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace RdpManager.Rdp;

/// <summary>
/// VNC oturumu (MarcusW.VncClient, MIT). RdpHost'un aksine saf WPF: goruntu bir
/// WriteableBitmap, girdi WPF olaylarindan RFB mesajlarina cevriliyor.
/// Otomatik yeniden baglanmayi kutuphane yapiyor.
/// </summary>
public sealed class VncHost : UserControl, ISessionHost
{
    private const int MaxReconnectAttempts = 5;

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Border _overlay;
    private readonly TextBlock _overlayText;
    private readonly Button _reconnectButton;
    private readonly VncFramebuffer _framebuffer;

    /// <summary>Basili tutulan tus -> gonderilen keysym (birakirken ayni keysym gitmeli).</summary>
    private readonly Dictionary<Key, uint> _pressed = [];

    private RdpConnection _connection = null!;
    private RfbConnection? _rfb;
    private CancellationTokenSource? _connectCts;
    private RdpSessionStatus _status = RdpSessionStatus.NotInitialized;
    private MouseButtons _buttons;
    private bool _disposed;

    public VncHost()
    {
        Focusable = true;
        FocusVisualStyle = null;
        SetResourceReference(BackgroundProperty, "Brush.SessionBackdrop");

        _framebuffer = new VncFramebuffer(Dispatcher, bitmap => _image.Source = bitmap);

        _overlayText = new TextBlock
        {
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD8, 0xD8, 0xDA)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        _reconnectButton = new Button
        {
            Content = Loc.T("Yeniden baglan"),
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _reconnectButton.SetResourceReference(StyleProperty, "Button.Primary");
        _reconnectButton.Click += (_, _) => Reconnect();

        _overlay = new Border
        {
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _overlayText, _reconnectButton },
            },
        };
        _overlay.SetResourceReference(Border.BackgroundProperty, "Brush.SessionBackdrop");

        Content = new Grid { Children = { _image, _overlay } };

        Loaded += (_, _) =>
        {
            if (_rfb is null && _connectCts is null && !_disposed) _ = ConnectAsync();
        };
        LostKeyboardFocus += (_, _) => ReleaseAllKeys();
    }

    public RdpConnection Connection => _connection;

    public RdpSessionStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<RdpSessionStatus>? StatusChanged;
    public event EventHandler? Connected;
    public event EventHandler<RdpDisconnectInfo>? Disconnected;

    public void Initialize(RdpConnection connection) => _connection = connection;

    // ------------------------------------------------------------- baglanti

    private async Task ConnectAsync()
    {
        if (_disposed || string.IsNullOrWhiteSpace(_connection.Address)) return;

        var cts = _connectCts = new CancellationTokenSource();
        Status = RdpSessionStatus.Connecting;
        ShowOverlay(Loc.F("{0} baglaniyor...", _connection.DisplayName), showReconnect: false);

        try
        {
            var rfb = await new VncClient(NullLoggerFactory.Instance).ConnectAsync(new ConnectParameters
            {
                TransportParameters = new TcpTransportParameters { Host = _connection.Address.Trim(), Port = _connection.Port },
                AuthenticationHandler = new SavedPasswordHandler(_connection.UserName, _connection.Password),
                InitialRenderTarget = _framebuffer,
                AllowSharedConnection = true,
                MaxReconnectAttempts = _connection.EnableAutoReconnect ? MaxReconnectAttempts : 0,
            }, cts.Token);

            if (_disposed || cts.IsCancellationRequested)
            {
                _ = CloseAsync(rfb);
                return;
            }

            _rfb = rfb;
            rfb.PropertyChanged += OnRfbPropertyChanged;
            ApplyState(rfb.ConnectionState);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Kullanici kapatti / yeniden baglaniyor.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Conn}] VNC baglantisi kurulamadi", _connection.DisplayName);
            var message = Loc.F("Baglanti kurulamadi: {0}", ex.Message);
            Status = RdpSessionStatus.Disconnected;
            ShowOverlay(message, showReconnect: true);
            Disconnected?.Invoke(this, new RdpDisconnectInfo(0, 0, message, false, ex is System.Net.Sockets.SocketException));
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
            cts.Dispose();
        }
    }

    private void OnRfbPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RfbConnection.ConnectionState) || sender is not RfbConnection rfb) return;
        var state = rfb.ConnectionState;
        // Kutuphane kendi thread'inden bildiriyor.
        Dispatcher.BeginInvoke(() => { if (ReferenceEquals(rfb, _rfb)) ApplyState(state); });
    }

    private void ApplyState(ConnectionState state)
    {
        var wasConnected = Status == RdpSessionStatus.Connected;

        switch (state)
        {
            case ConnectionState.Connected:
                Status = RdpSessionStatus.Connected;
                _overlay.Visibility = Visibility.Collapsed;
                Focus();
                Connected?.Invoke(this, EventArgs.Empty);
                break;

            case ConnectionState.Interrupted or ConnectionState.Reconnecting or ConnectionState.ReconnectFailed:
                Status = RdpSessionStatus.Connecting;
                ShowOverlay(Loc.T("Baglanti koptu, yeniden baglaniliyor..."), showReconnect: false);
                if (wasConnected)
                    Disconnected?.Invoke(this, new RdpDisconnectInfo(0, 0, Loc.T("Baglanti koptu, yeniden baglaniliyor..."), false, true));
                break;

            case ConnectionState.Closed:
                Status = RdpSessionStatus.Disconnected;
                ShowOverlay(Loc.T("Baglanti kesildi"), showReconnect: true);
                if (wasConnected)
                    Disconnected?.Invoke(this, new RdpDisconnectInfo(0, 0, Loc.T("Baglanti kesildi"), false, false));
                DropConnection();
                break;
        }
    }

    public void Reconnect()
    {
        if (_disposed) return;
        DropConnection();
        _ = ConnectAsync();
    }

    public void Disconnect() => DropConnection();

    private void DropConnection()
    {
        _connectCts?.Cancel();
        var rfb = _rfb;
        _rfb = null;
        if (rfb is null) return;
        rfb.PropertyChanged -= OnRfbPropertyChanged;
        _ = CloseAsync(rfb);
    }

    private static async Task CloseAsync(RfbConnection rfb)
    {
        try { await rfb.CloseAsync(); }
        catch (Exception ex) { Log.Debug(ex, "VNC kapatilirken hata"); }
        finally { rfb.Dispose(); }
    }

    public void DisposeHost()
    {
        if (_disposed) return;
        _disposed = true;
        DropConnection();
        _framebuffer.Dispose();
    }

    public bool CaptureThumbnail()
        => Status == RdpSessionStatus.Connected && _framebuffer.SaveThumbnail(_connection.Id);

    private void ShowOverlay(string text, bool showReconnect)
    {
        _overlayText.Text = text;
        _reconnectButton.Visibility = showReconnect ? Visibility.Visible : Visibility.Collapsed;
        _overlay.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- girdi

    private void SendKey(bool down, uint keysym)
        => _rfb?.EnqueueMessage(new KeyEventMessage(down, (KeySymbol)keysym), default);

    public void SendCtrlAltDelete()
    {
        foreach (var k in new[] { VncKeyMap.ControlL, VncKeyMap.AltL, VncKeyMap.Delete }) SendKey(true, k);
        foreach (var k in new[] { VncKeyMap.Delete, VncKeyMap.AltL, VncKeyMap.ControlL }) SendKey(false, k);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_rfb is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrlOrAlt = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;

        if (VncKeyMap.TryGetSpecial(key, out var keysym)
            || (ctrlOrAlt && !VncKeyMap.ProducesText(key) && VncKeyMap.TryGetShortcut(key, out keysym)))
        {
            _pressed[key] = keysym;
            SendKey(true, keysym);
            e.Handled = true; // Tab/oklar WPF odak gezintisine gitmesin
        }
        // Digerleri TextInput ile karakter olarak gelir.
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_pressed.Remove(key, out var keysym))
        {
            SendKey(false, keysym);
            e.Handled = true;
        }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (_rfb is null || string.IsNullOrEmpty(e.Text)) return;

        // AltGr (Ctrl+Alt) ile uretilen karakterde (@, {, € ...) sunucu Ctrl+Alt+karakter gormesin.
        var held = _pressed.Values.Where(k => k is VncKeyMap.ControlL or VncKeyMap.ControlR or VncKeyMap.AltL or VncKeyMap.AltR).ToList();
        foreach (var k in held) SendKey(false, k);

        foreach (var c in e.Text.Where(c => !char.IsControl(c)))
        {
            var keysym = VncKeyMap.FromChar(c);
            SendKey(true, keysym);
            SendKey(false, keysym);
        }

        foreach (var k in held) SendKey(true, k);
        e.Handled = true;
    }

    private void ReleaseAllKeys()
    {
        foreach (var keysym in _pressed.Values) SendKey(false, keysym);
        _pressed.Clear();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SendPointer(e);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _buttons |= ToVnc(e.ChangedButton);
        CaptureMouse();
        SendPointer(e);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _buttons &= ~ToVnc(e.ChangedButton);
        if (_buttons == MouseButtons.None) ReleaseMouseCapture();
        SendPointer(e);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // RFB'de tekerlek: tekerlek "dugmesine" bas ve birak.
        var wheel = e.Delta > 0 ? MouseButtons.WheelUp : MouseButtons.WheelDown;
        SendPointer(e, _buttons | wheel);
        SendPointer(e);
        e.Handled = true;
    }

    private static MouseButtons ToVnc(MouseButton button) => button switch
    {
        MouseButton.Left => MouseButtons.Left,
        MouseButton.Middle => MouseButtons.Middle,
        MouseButton.Right => MouseButtons.Right,
        _ => MouseButtons.None,
    };

    /// <summary>Ekrandaki (olceklenmis) konumu uzak ekran pikseline cevirir.</summary>
    private void SendPointer(MouseEventArgs e, MouseButtons? buttons = null)
    {
        if (_rfb is null || _image.ActualWidth < 1 || _image.ActualHeight < 1) return;
        var (width, height) = _framebuffer.PixelSize;
        if (width == 0) return;

        var p = e.GetPosition(_image);
        var x = Math.Clamp((int)(p.X * width / _image.ActualWidth), 0, width - 1);
        var y = Math.Clamp((int)(p.Y * height / _image.ActualHeight), 0, height - 1);
        _rfb.EnqueueMessage(new PointerEventMessage(new Position(x, y), buttons ?? _buttons), default);
    }

    /// <summary>Kayitli parolayi verir; parola yoksa net bir hata ile durur (kullaniciya sormak yerine).</summary>
    private sealed class SavedPasswordHandler(string userName, string? password) : IAuthenticationHandler
    {
        public Task<TInput> ProvideAuthenticationInputAsync<TInput>(
            RfbConnection connection, ISecurityType securityType, IAuthenticationInputRequest<TInput> request)
            where TInput : class, IAuthenticationInput
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException(Loc.T("Sunucu parola istiyor. Baglanti editorunden VNC parolasini kaydedin."));

            IAuthenticationInput input = typeof(TInput) == typeof(CredentialsAuthenticationInput)
                ? new CredentialsAuthenticationInput(userName, password)
                : new PasswordAuthenticationInput(password);

            return input is TInput typed
                ? Task.FromResult(typed)
                : throw new NotSupportedException(Loc.F("Desteklenmeyen VNC kimlik dogrulamasi: {0}", securityType.Name));
        }
    }
}
