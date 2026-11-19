using RdpManager.Rdp;
using Xunit;

namespace RdpManager.Tests;

public class SshKnownHostsTests
{
    private readonly Dictionary<string, string> _settings = [];
    private SshKnownHosts Store() => new(k => _settings.GetValueOrDefault(k), (k, v) => _settings[k] = v);

    [Fact]
    public void First_connection_is_new_then_trusted_after_saving()
    {
        var store = Store();
        Assert.Equal(HostKeyStatus.New, store.Check("Srv01", 22, "ssh-ed25519", "abc", out _));

        store.Trust("Srv01", 22, "ssh-ed25519", "abc");

        // Ad buyuk/kucuk harf duyarsiz; ayni anahtar guvenilir.
        Assert.Equal(HostKeyStatus.Trusted, store.Check("srv01", 22, "ssh-ed25519", "abc", out _));
    }

    [Fact]
    public void Different_key_is_reported_as_changed_with_saved_value()
    {
        var store = Store();
        store.Trust("srv01", 22, "ssh-ed25519", "abc");

        Assert.Equal(HostKeyStatus.Changed, store.Check("srv01", 22, "ssh-ed25519", "xyz", out var saved));
        Assert.Equal("ssh-ed25519 abc", saved);
    }

    [Fact]
    public void Ports_are_tracked_separately()
    {
        var store = Store();
        store.Trust("srv01", 22, "ssh-ed25519", "abc");
        Assert.Equal(HostKeyStatus.New, store.Check("srv01", 2222, "ssh-ed25519", "abc", out _));
    }
}
