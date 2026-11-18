namespace RdpManager.Rdp;

public enum RdpSessionStatus
{
    /// <summary>ActiveX kontrolu henuz olusturulmadi.</summary>
    NotInitialized,

    /// <summary>Connect() cagrildi, sunucudan yanit bekleniyor. Bu sirada resize BLOKLU.</summary>
    Connecting,

    Connected,

    Disconnected
}
