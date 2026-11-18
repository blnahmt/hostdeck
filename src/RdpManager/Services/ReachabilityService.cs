using System.Net.Sockets;
using System.Windows.Threading;
using RdpManager.Models;

namespace RdpManager.Services;

/// <summary>
/// Sunucularin acik olup olmadigini periyodik olarak yoklar ve
/// <see cref="RdpConnection.IsReachable"/> alanini gunceller.
///
/// ICMP ping yerine baglanti portuna TCP baglantisi deneniyor: cogu sunucuda
/// ICMP kapali, ama RDP/SSH portu aciksa sunucu gercekten kullanilabilir demektir.
/// </summary>
public sealed class ReachabilityService : IDisposable
{
    private const int TimeoutMs = 1500;
    private const int GatewayPort = 443;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(30),
    };

    private Func<IReadOnlyList<RdpConnection>>? _source;
    private bool _busy;

    public void Start(Func<IReadOnlyList<RdpConnection>> source)
    {
        _source = source;
        _timer.Tick += async (_, _) => await CheckAllAsync();
        _timer.Start();
        _ = CheckAllAsync();
    }

    public async Task CheckAllAsync()
    {
        if (_busy || _source is null) return;
        _busy = true;
        try
        {
            var list = _source().Where(c => !string.IsNullOrWhiteSpace(c.Address)).ToList();
            // Gateway arkasindaki sunucuya dogrudan erisilemez; gateway'in kendisi yoklanir.
            var results = await Task.WhenAll(list.Select(c => string.IsNullOrWhiteSpace(c.GatewayHostname)
                ? IsPortOpenAsync(c.Address, c.Port)
                : IsPortOpenAsync(c.GatewayHostname, GatewayPort)));

            // await UI thread'inde devam ediyor (DispatcherTimer baglami).
            for (var i = 0; i < list.Count; i++) list[i].IsReachable = results[i];
        }
        finally
        {
            _busy = false;
        }
    }

    public static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeoutMs);
            await client.ConnectAsync(host.Trim(), port, cts.Token);
            return true;
        }
        catch
        {
            return false; // zaman asimi, reddedildi, cozulemeyen ad: hepsi "erisilemiyor"
        }
    }

    public void Dispose() => _timer.Stop();
}
