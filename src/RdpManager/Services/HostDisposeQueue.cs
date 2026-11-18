using System.Collections.Concurrent;
using System.Windows.Threading;
using RdpManager.Rdp;
using Serilog;

namespace RdpManager.Services;

/// <summary>
/// RDP ActiveX host'larini GECIKTIREREK dispose eder.
///
/// Neden: oturum koptugu anda (OnDisconnected callback'i icindeyken) kontrolu
/// dispose etmek, ActiveX kendi mesaj dongusunun ortasinda oldugu icin
/// uygulamayi cokertiyor. Host'u kuyruga at, birkac saniye sonra temizle.
/// </summary>
public sealed class HostDisposeQueue : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<HostDisposeQueue>();
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    private readonly ConcurrentQueue<(ISessionHost Host, DateTime EnqueuedAt)> _queue = new();
    private readonly DispatcherTimer _timer;

    public HostDisposeQueue()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Drain();
        _timer.Start();
    }

    public void Enqueue(ISessionHost host)
    {
        _queue.Enqueue((host, DateTime.UtcNow));
        Logger.Debug("Host dispose kuyruguna alindi: {Conn}", host.Connection.DisplayName);
    }

    private void Drain()
    {
        var now = DateTime.UtcNow;
        while (_queue.TryPeek(out var item) && now - item.EnqueuedAt >= Delay)
        {
            if (!_queue.TryDequeue(out item)) break;
            try
            {
                item.Host.DisposeHost();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Geciktirilmis dispose basarisiz");
            }
        }
    }

    /// <summary>Uygulama kapanirken: bekleyen her seyi hemen temizle.</summary>
    public void Dispose()
    {
        _timer.Stop();
        while (_queue.TryDequeue(out var item))
        {
            try { item.Host.DisposeHost(); }
            catch (Exception ex) { Logger.Warning(ex, "Kapanista dispose hatasi"); }
        }
    }
}
