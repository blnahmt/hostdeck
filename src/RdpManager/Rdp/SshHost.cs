using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RdpManager.Data;
using RdpManager.Localization;
using RdpManager.Models;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using Serilog;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace RdpManager.Rdp;

/// <summary>
/// Sekme icinde SSH terminali: baglanti SSH.NET (MIT), goruntu WebView2 + xterm.js (MIT).
/// Parola veya anahtar dosyasiyla giris; sunucu anahtari ilk kullanimda onaylatilir.
/// </summary>
public sealed class SshHost : UserControl, ISessionHost
{
    private const string VirtualHost = "terminal.hostline";
    private const int ReplayLimit = 256 * 1024;

    private static Task<CoreWebView2Environment>? _environment;

    private readonly WebView2 _web = new();
    private readonly Border _overlay;
    private readonly TextBlock _overlayText;
    private readonly Button _reconnectButton;

    /// <summary>Sunucu ciktisinin son kismi: sayfa yeniden yuklenirse ekran geri cizilir.</summary>
    private readonly StringBuilder _replay = new();
    private readonly StringBuilder _pending = new();
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();
    private readonly object _outputLock = new();

    private RdpConnection _connection = null!;
    private SshClient? _client;
    private ShellStream? _shell;
    private RdpSessionStatus _status = RdpSessionStatus.NotInitialized;
    private bool _pageReady, _flushQueued, _connecting, _disposed, _started;
    // SSH.NET thread'inde yazilir, UI'da okunur.
    private volatile bool _keyJustTrusted, _hostKeyRejected;
    private volatile string? _connectError;
    private volatile CancellationTokenSource? _connectCts;
    private readonly TaskCompletionSource _pageReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _cols = 120, _rows = 30;

    public SshHost()
    {
        SetResourceReference(BackgroundProperty, "Brush.SessionBackdrop");
        _web.DefaultBackgroundColor = System.Drawing.Color.Black;

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
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _overlayText, _reconnectButton } },
        };
        _overlay.SetResourceReference(Border.BackgroundProperty, "Brush.SessionBackdrop");

        Content = new Grid { Children = { _web, _overlay } };

        Loaded += async (_, _) =>
        {
            if (_started || _disposed) return;
            _started = true;
            await InitializeTerminalAsync();
            await ConnectAsync();
        };
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

    // ------------------------------------------------------------- terminal

    private async Task InitializeTerminalAsync()
    {
        try
        {
            // Tum sekmeler tek tarayici sureci paylassin; veriler uygulama klasorunde kalsin.
            _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataDirectory, "WebView2"));
            await _web.EnsureCoreWebView2Async(await _environment);

            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false; // F5/Ctrl+R sayfayi yenilemesin
            core.SetVirtualHostNameToFolderMapping(VirtualHost,
                Path.Combine(AppContext.BaseDirectory, "Terminal"), CoreWebView2HostResourceAccessKind.Deny);
            // Terminal sayfasindan baska yere gidilmesin (or. ciktidaki bir baglanti).
            core.NavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith($"https://{VirtualHost}/", StringComparison.Ordinal);
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += OnWebMessage;
            core.Navigate($"https://{VirtualHost}/terminal.html");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Conn}] terminal baslatilamadi", _connection.DisplayName);
            ShowOverlay(Loc.F("Terminal baslatilamadi (WebView2 gerekli): {0}", ex.Message), showReconnect: false);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "ready":
                    _pageReady = true;
                    _pageReadyTcs.TrySetResult();
                    lock (_outputLock) { _pending.Clear(); _pending.Append(_replay); }
                    QueueFlush();
                    break;
                case "in":
                    if (_shell is { } shell)
                    {
                        shell.Write(root.GetProperty("d").GetString());
                        shell.Flush();
                    }
                    break;
                case "size":
                    _cols = (uint)Math.Max(1, root.GetProperty("cols").GetInt32());
                    _rows = (uint)Math.Max(1, root.GetProperty("rows").GetInt32());
                    _shell?.ChangeWindowSize(_cols, _rows, 0, 0);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[{Conn}] terminal mesaji islenemedi", _connection.DisplayName);
        }
    }

    /// <summary>
    /// Sunucu ciktisini surekli okur. DataReceived olayi yerine okuma: kabuk acilirken gelen ilk
    /// veri (giris mesaji, komut istemi) kaybolmaz ve SSH.NET'in ic tamponu birikmez.
    /// </summary>
    private async Task ReadLoopAsync(ShellStream shell)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int count;
            while ((count = await shell.ReadAsync(buffer)) > 0) OnOutput(buffer, count);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SshException or IOException)
        {
            // Baglanti kapatildi.
        }
        _ = Dispatcher.BeginInvoke(() => { if (ReferenceEquals(shell, _shell)) OnConnectionLost(Loc.T("Baglanti kesildi")); });
    }

    /// <summary>SSH okuma thread'inden: metni biriktir, UI'da tek mesajla gonder.</summary>
    private void OnOutput(byte[] data, int count)
    {
        lock (_outputLock)
        {
            var chars = new char[_utf8.GetCharCount(data, 0, count)];
            _utf8.GetChars(data, 0, count, chars, 0); // durumlu: bolunmus cok baytli karakterler korunur
            _pending.Append(chars);
            _replay.Append(chars);
            if (_replay.Length > ReplayLimit) _replay.Remove(0, _replay.Length - ReplayLimit);
        }
        QueueFlush();
    }

    private void QueueFlush()
    {
        lock (_outputLock)
        {
            if (_flushQueued) return;
            _flushQueued = true;
        }
        Dispatcher.BeginInvoke(() =>
        {
            string text;
            lock (_outputLock)
            {
                _flushQueued = false;
                if (!_pageReady || _pending.Length == 0) return;
                text = _pending.ToString();
                _pending.Clear();
            }
            if (!_disposed) _web.CoreWebView2?.PostWebMessageAsString(text);
        });
    }

    // ------------------------------------------------------------- baglanti

    private async Task ConnectAsync()
    {
        if (_disposed || _connecting || string.IsNullOrWhiteSpace(_connection.Address)) return;
        _connecting = true;
        _keyJustTrusted = _hostKeyRejected = false;
        _connectError = null;
        Status = RdpSessionStatus.Connecting;
        ShowOverlay(Loc.F("{0} baglaniyor...", _connection.DisplayName), showReconnect: false);

        // SSH.NET anahtar reddi / arka plan hatasinda ConnectAsync'i bitirmiyor; elle iptal ediyoruz.
        var cts = _connectCts = new CancellationTokenSource();
        SshClient? client = null;
        try
        {
            client = new SshClient(BuildConnectionInfo());
            client.HostKeyReceived += OnHostKeyReceived;
            client.ErrorOccurred += (_, e) =>
            {
                if (_connecting) { _connectError = e.Exception.Message; CancelConnect(cts); }
                else Dispatcher.BeginInvoke(() => OnConnectionLost(e.Exception.Message));
            };
            // WaitAsync: SSH.NET anahtar reddinde kendi icinde takilabiliyor; iptalde beklemeyi birak.
            await client.ConnectAsync(cts.Token).WaitAsync(cts.Token);

            // Terminal boyutu belli olsun diye sayfanin hazir olmasini kisa sure bekle.
            await Task.WhenAny(_pageReadyTcs.Task, Task.Delay(3000));
            var (cols, rows) = (_cols, _rows);
            var shell = await Task.Run(() => client.CreateShellStream("xterm-256color", cols, rows, 0, 0, 64 * 1024));

            if (_disposed) { client.Dispose(); return; }
            _client = client;
            _shell = shell;

            Status = RdpSessionStatus.Connected;
            _overlay.Visibility = Visibility.Collapsed;
            _web.Visibility = Visibility.Visible;
            _web.Focus();
            Connected?.Invoke(this, EventArgs.Empty);
            _ = Task.Run(() => ReadLoopAsync(shell));
        }
        catch (Exception ex) when (_keyJustTrusted && ex is SshOperationTimeoutException or OperationCanceledException)
        {
            // Kullanici parmak izini incelerken el sikismasi zaman asimina ugradi.
            // Anahtar artik kayitli: soru sormadan hemen bir kez daha dene.
            DisposeInBackground(client); // takili istemci UI thread'ini kilitlemesin
            _connecting = false;
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            DisposeInBackground(client); // takili istemci UI thread'ini kilitlemesin
            Log.Warning(ex, "[{Conn}] SSH baglantisi kurulamadi", _connection.DisplayName);
            var message = _hostKeyRejected || ex is SshConnectionException { DisconnectReason: DisconnectReason.HostKeyNotVerifiable }
                ? Loc.T("Sunucu anahtari onaylanmadigi icin baglanilmadi.")
                : ex is SshAuthenticationException
                    ? Loc.T("Kimlik dogrulama basarisiz: kullanici adi, parola veya anahtar hatali.")
                    : Loc.F("Baglanti kurulamadi: {0}", ex is OperationCanceledException && _connectError is { } error ? error : ex.Message);
            Status = RdpSessionStatus.Disconnected;
            ShowOverlay(message, showReconnect: true);
            Disconnected?.Invoke(this, new RdpDisconnectInfo(0, 0, message, false, ex is System.Net.Sockets.SocketException));
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
            cts.Dispose();
            _connecting = false;
        }
    }

    private static void DisposeInBackground(SshClient? client)
    {
        if (client is null) return;
        Task.Run(() =>
        {
            try { client.Dispose(); }
            catch (Exception ex) { Log.Debug(ex, "SSH istemcisi kapatilamadi"); }
        });
    }

    private static void CancelConnect(CancellationTokenSource cts)
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { } // deneme zaten bitti
    }

    private ConnectionInfo BuildConnectionInfo()
    {
        var c = _connection;
        var user = c.UserName.Trim();
        if (user.Length == 0) throw new InvalidOperationException(Loc.T("SSH icin kullanici adi gerekli."));

        AuthenticationMethod[] methods;
        if (c.SshUseKey)
        {
            if (!File.Exists(c.SshKeyPath))
                throw new InvalidOperationException(Loc.F("Anahtar dosyasi bulunamadi: {0}", c.SshKeyPath));
            var key = string.IsNullOrEmpty(c.Password) ? new PrivateKeyFile(c.SshKeyPath) : new PrivateKeyFile(c.SshKeyPath, c.Password);
            methods = [new PrivateKeyAuthenticationMethod(user, key)];
        }
        else
        {
            if (string.IsNullOrEmpty(c.Password))
                throw new InvalidOperationException(Loc.T("Parola kayitli degil. Baglanti editorunden parolayi kaydedin veya SSH anahtari kullanin."));
            // Bircok sunucu parolayi "keyboard-interactive" ile soruyor; ayni parolayla yanitla.
            var interactive = new KeyboardInteractiveAuthenticationMethod(user);
            interactive.AuthenticationPrompt += (_, e) => { foreach (var prompt in e.Prompts) prompt.Response = c.Password; };
            methods = [new PasswordAuthenticationMethod(user, c.Password), interactive];
        }

        return new ConnectionInfo(c.Address.Trim(), c.Port, user, methods) { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>SSH.NET'in baglanti thread'inde cagrilir; karar icin UI'da kullaniciya sorulur.</summary>
    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var host = _connection.Address.Trim();
        var port = _connection.Port;
        var known = new SshKnownHosts(
            k => App.Services.GetRequiredService<ConnectionRepository>().GetSetting(k),
            (k, v) => App.Services.GetRequiredService<ConnectionRepository>().SetSetting(k, v));

        var status = known.Check(host, port, e.HostKeyName, e.FingerPrintSHA256, out var saved);
        if (status == HostKeyStatus.Trusted)
        {
            e.CanTrust = true;
            return;
        }

        e.CanTrust = Dispatcher.Invoke(() =>
        {
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            var answer = status == HostKeyStatus.New
                ? MessageBox.Show(owner,
                    Loc.F("'{0}' sunucusuna ilk kez baglaniliyor.\n\nAnahtar: {1}\nSHA256: {2}\n\nBu anahtar sunucuya aitse onaylayin. Sunucuya guvenip baglanilsin mi?", host, e.HostKeyName, e.FingerPrintSHA256),
                    Loc.T("SSH sunucu anahtari"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show(owner,
                    Loc.F("UYARI: '{0}' sunucusunun anahtari DEGISTI.\n\nKayitli: {1}\nSimdiki: {2} {3}\n\nSunucu yeniden kurulduysa bu normaldir; degilse araya biri girmis olabilir. Yeni anahtara guvenip baglanilsin mi?", host, saved ?? "", e.HostKeyName, e.FingerPrintSHA256),
                    Loc.T("SSH sunucu anahtari degisti"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        });

        if (!e.CanTrust)
        {
            _hostKeyRejected = true;
            if (_connectCts is { } cts) CancelConnect(cts);
            return;
        }
        known.Trust(host, port, e.HostKeyName, e.FingerPrintSHA256);
        _keyJustTrusted = true;
    }

    private void OnConnectionLost(string reason)
    {
        if (_disposed || Status != RdpSessionStatus.Connected) return;
        Status = RdpSessionStatus.Disconnected;
        ShowOverlay(reason, showReconnect: true);
        Disconnected?.Invoke(this, new RdpDisconnectInfo(0, 0, reason, false, true));
        DropConnection();
    }

    public void Reconnect()
    {
        if (_disposed) return;
        DropConnection();
        Status = RdpSessionStatus.Disconnected;
        _ = ConnectAsync();
    }

    public void Disconnect() => DropConnection();

    private void DropConnection()
    {
        var (shell, client) = (_shell, _client);
        (_shell, _client) = (null, null);
        // Kapatma ag beklemesi yapabilir; UI donmasin.
        if (shell is not null || client is not null)
            Task.Run(() =>
            {
                try { shell?.Dispose(); client?.Disconnect(); client?.Dispose(); }
                catch (Exception ex) { Log.Debug(ex, "SSH kapatilirken hata"); }
            });
    }

    public void DisposeHost()
    {
        if (_disposed) return;
        _disposed = true;
        DropConnection();
        _web.Dispose();
    }

    public void SendCtrlAltDelete() { } // SSH'ta karsiligi yok
    public bool CaptureThumbnail() => false;

    private void ShowOverlay(string text, bool showReconnect)
    {
        _overlayText.Text = text;
        _reconnectButton.Visibility = showReconnect ? Visibility.Visible : Visibility.Collapsed;
        _overlay.Visibility = Visibility.Visible;
        // WebView2 bir HWND: WPF katmani ustune cizilemez, gizlenmeli (airspace).
        _web.Visibility = Visibility.Hidden;
    }
}
