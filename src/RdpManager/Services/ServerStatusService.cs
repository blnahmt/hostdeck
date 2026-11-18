using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Threading;
using RdpManager.Data;
using RdpManager.Models;
using Serilog;

namespace RdpManager.Services;

/// <summary>
/// Sirket ici sunucu durum API'sini periyodik olarak cagirip
/// baglanti listesindeki <see cref="RdpConnection.RemoteUser"/> alanini gunceller.
/// </summary>
public sealed class ServerStatusService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ServerStatusService>();

    private const string SettingKey = "api.key";
    private const int PollIntervalSeconds = 10;

    private readonly ConnectionRepository _repository;
    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;

    private Func<IReadOnlyList<RdpConnection>>? _connectionsSource;

    /// <summary>
    /// Son API cevabindaki IP -> clientUser eslemesi. Launcher ve hizli baglanti
    /// listeleri kendi (taze yuklenmis) nesnelerine bunu uyguluyor.
    /// </summary>
    private volatile Dictionary<string, string?> _lastMap =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Her basarili yoklamadan sonra tetiklenir (UI thread'inde).</summary>
    public event EventHandler? StatusChanged;

    public ServerStatusService(ConnectionRepository repository)
    {
        _repository = repository;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
#if !PUBLIC_EDITION
        // Sirket ici API: dis surum exe'sinde bu adres hic bulunmamali.
        _http.BaseAddress = new Uri(InternalEdition.ServerStatusUrl);
#endif

        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(PollIntervalSeconds),
        };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public string? ApiKey
    {
        get => _repository.GetSetting(SettingKey);
        set
        {
            _repository.SetSetting(SettingKey, value ?? string.Empty);
            UpdateHttpHeader(value);

            if (string.IsNullOrWhiteSpace(value))
                Stop();
            else if (!_timer.IsEnabled)
                Start();
        }
    }

    public void Initialize(Func<IReadOnlyList<RdpConnection>> connectionsSource)
    {
        _connectionsSource = connectionsSource;
        if (!AppEdition.HasServerStatus) return;

        var key = ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            UpdateHttpHeader(key);
            Start();
        }
    }

    public void RefreshNow() => _ = PollAsync();

    /// <summary>Son bilinen duruma gore verilen adreste bagli kullanici (yoksa null).</summary>
    public string? GetRemoteUser(string? address)
    {
        if (string.IsNullOrEmpty(address)) return null;
        var host = address.Split(':')[0].Trim();
        return _lastMap.TryGetValue(host, out var user) ? user : null;
    }

    private void Start()
    {
        _timer.Start();
        _ = PollAsync();
    }

    private void Stop()
    {
        _timer.Stop();
        ClearAll();
    }

    private void UpdateHttpHeader(string? key)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(key)
            ? null
            : new AuthenticationHeaderValue("Bearer", key.Trim());
    }

    private async Task PollAsync()
    {
        if (_connectionsSource is null || !AppEdition.HasServerStatus) return;

        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return;

        try
        {
            var response = await _http.GetAsync("/api/public/servers");

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    Logger.Warning("Sunucu durumu API: gecersiz anahtar (401)");
                else
                    Logger.Warning("Sunucu durumu API: HTTP {Status}", (int)response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var serverMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.TryGetProperty("servers", out var servers))
            {
                foreach (var server in servers.EnumerateArray())
                {
                    var ip = server.TryGetProperty("ipAddress", out var ipProp)
                        ? ipProp.GetString() : null;
                    if (string.IsNullOrEmpty(ip)) continue;

                    var clientUser = server.TryGetProperty("clientUser", out var userProp)
                        ? userProp.GetString() : null;

                    serverMap[ip] = clientUser;
                }
            }

            _dispatcher.Invoke(() => ApplyStatus(serverMap));
        }
        catch (TaskCanceledException)
        {
            // Timeout — sessizce atla.
        }
        catch (HttpRequestException ex)
        {
            Logger.Debug(ex, "Sunucu durumu API erisim hatasi");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Sunucu durumu API beklenmeyen hata");
        }
    }

    private void ApplyStatus(Dictionary<string, string?> serverMap)
    {
        _lastMap = serverMap;

        if (_connectionsSource is not null)
            ApplyTo(_connectionsSource());

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Verilen baglantilara son bilinen sunucu durumunu uygular.
    /// Launcher/hizli baglanti gibi kendi nesnelerini yukleyen gorunumler icin.
    /// </summary>
    public void ApplyTo(IEnumerable<RdpConnection> connections)
    {
        var map = _lastMap;

        foreach (var connection in connections)
        {
            if (string.IsNullOrEmpty(connection.Address)) continue;

            var address = connection.Address.Split(':')[0].Trim();

            connection.RemoteUser = map.TryGetValue(address, out var user)
                ? user
                : null;
        }
    }

    private void ClearAll()
    {
        _lastMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (_connectionsSource is null) return;
        foreach (var connection in _connectionsSource())
            connection.RemoteUser = null;

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }
}
