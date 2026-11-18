using RdpManager.Models;

namespace RdpManager.Rdp;

/// <summary>
/// Bir sekmedeki oturum (RDP veya VNC). Uygulayanlar UserControl'dur; sekme,
/// tam ekran ve dispose kuyrugu yalniz bu yuzeyi kullanir.
/// </summary>
public interface ISessionHost
{
    RdpConnection Connection { get; }
    RdpSessionStatus Status { get; }

    event EventHandler<RdpSessionStatus>? StatusChanged;
    event EventHandler? Connected;
    event EventHandler<RdpDisconnectInfo>? Disconnected;

    void Initialize(RdpConnection connection);
    void Reconnect();
    void Disconnect();
    void SendCtrlAltDelete();

    /// <summary>Gorunur ve bagliyken son goruntuyu kucuk resim olarak kaydeder.</summary>
    bool CaptureThumbnail();

    /// <summary>Kaynaklari birakir. <see cref="Services.HostDisposeQueue"/> uzerinden gecikmeli cagrilir.</summary>
    void DisposeHost();
}
