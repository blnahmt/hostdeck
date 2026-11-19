namespace RdpManager.Rdp;

public enum HostKeyStatus
{
    /// <summary>Bu sunucuya ilk kez baglaniliyor.</summary>
    New,
    /// <summary>Kayitli anahtarla ayni.</summary>
    Trusted,
    /// <summary>Kayitli anahtar farkli: sunucu yeniden kurulmus ya da araya biri girmis olabilir.</summary>
    Changed,
}

/// <summary>
/// SSH sunucu anahtarlari icin "ilk kullanimda guven" deposu (OpenSSH known_hosts gibi).
/// Ayarlar tablosunda host:port basina "algoritma SHA256-parmak-izi" saklanir.
/// </summary>
public sealed class SshKnownHosts(Func<string, string?> getSetting, Action<string, string> setSetting)
{
    private static string Key(string host, int port) => $"ssh.hostkey.{host.Trim().ToLowerInvariant()}:{port}";

    public HostKeyStatus Check(string host, int port, string algorithm, string fingerprint, out string? saved)
    {
        saved = getSetting(Key(host, port));
        if (string.IsNullOrEmpty(saved)) return HostKeyStatus.New;
        return saved == $"{algorithm} {fingerprint}" ? HostKeyStatus.Trusted : HostKeyStatus.Changed;
    }

    public void Trust(string host, int port, string algorithm, string fingerprint)
        => setSetting(Key(host, port), $"{algorithm} {fingerprint}");
}
