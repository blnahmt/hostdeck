using System.IO;
using RdpManager.Data;
using Xunit;

namespace RdpManager.Tests;

public sealed class RdpFileImporterTests : IDisposable
{
    private readonly List<string> _files = [];

    private string WriteRdp(string name, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.rdp");
        File.WriteAllLines(path, lines);
        _files.Add(path);
        return path;
    }

    [Fact]
    public void Parses_address_and_port()
    {
        var path = WriteRdp("Sunucu", "full address:s:192.168.1.50:3390");

        var result = RdpFileImporter.Import(path);

        Assert.Equal("192.168.1.50", result.Address);
        Assert.Equal(3390, result.Port);
    }

    [Fact]
    public void Gateway_is_imported_only_when_used()
    {
        var used = RdpFileImporter.Import(WriteRdp("Gw", "full address:s:10.0.0.5",
            "gatewayhostname:s:gw.example.com", "gatewayusagemethod:i:1"));
        var unused = RdpFileImporter.Import(WriteRdp("NoGw", "full address:s:10.0.0.5",
            "gatewayhostname:s:gw.example.com", "gatewayusagemethod:i:4"));

        Assert.Equal("gw.example.com", used.GatewayHostname);
        Assert.Equal(string.Empty, unused.GatewayHostname);
    }

    [Fact]
    public void Defaults_to_3389_when_no_port_given()
    {
        var path = WriteRdp("Sunucu", "full address:s:fileserver.local");

        var result = RdpFileImporter.Import(path);

        Assert.Equal("fileserver.local", result.Address);
        Assert.Equal(3389, result.Port);
    }

    [Fact]
    public void Ipv6_address_without_port_is_not_split()
    {
        var path = WriteRdp("Sunucu", "full address:s:[fe80::1]");

        var result = RdpFileImporter.Import(path);

        Assert.Equal("[fe80::1]", result.Address);
        Assert.Equal(3389, result.Port);
    }

    [Fact]
    public void Splits_domain_qualified_username()
    {
        var path = WriteRdp("Sunucu",
            "full address:s:srv01",
            @"username:s:SIRKET\kullanici");

        var result = RdpFileImporter.Import(path);

        Assert.Equal("SIRKET", result.Domain);
        Assert.Equal("kullanici", result.UserName);
    }

    [Fact]
    public void Maps_redirection_and_display_flags()
    {
        var path = WriteRdp("Sunucu",
            "full address:s:srv01",
            "session bpp:i:24",
            "audiomode:i:2",
            "redirectprinters:i:1",
            "redirectcomports:i:0",
            "drivestoredirect:s:*",
            "smart sizing:i:1",
            "authentication level:i:0");

        var result = RdpFileImporter.Import(path);

        Assert.Equal(24, result.ColorDepth);
        Assert.Equal(2, result.AudioRedirectionMode);
        Assert.True(result.RedirectPrinters);
        Assert.False(result.RedirectPorts);
        Assert.True(result.RedirectDrives);
        Assert.True(result.SmartSizing);
        Assert.Equal(0, result.AuthenticationLevel);
    }

    [Fact]
    public void Encrypted_password_field_is_ignored()
    {
        var path = WriteRdp("Sunucu",
            "full address:s:srv01",
            "password 51:b:01000000D08C9DDF0115D1118C7A00C04FC297EB");

        var result = RdpFileImporter.Import(path);

        // .rdp parolalari makineye ozel DPAPI blob'u — tasinabilir degil, alinmiyor.
        Assert.Null(result.Password);
        Assert.Null(result.ProtectedPassword);
    }

    [Fact]
    public void Name_falls_back_to_file_name()
    {
        var path = WriteRdp("MuhasebeSunucusu", "full address:s:srv01");

        var result = RdpFileImporter.Import(path);

        Assert.StartsWith("MuhasebeSunucusu", result.Name);
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            try { File.Delete(file); } catch { /* temp */ }
        }
    }
}
