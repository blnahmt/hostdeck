using System.IO;
using RdpManager.Models;
using Serilog;

namespace RdpManager.Data;

/// <summary>
/// Standart .rdp dosyalarini okur. Ekipte zaten dagitilmis .rdp dosyalari varsa
/// elle yeniden girmeye gerek kalmiyor.
///
/// Format: her satir "anahtar:tip:deger" — tip s=metin, i=sayi, b=ikili.
/// Parolalar (password 51:b:) makineye ozel DPAPI blob'u oldugu icin
/// tasinabilir degil; BILEREK okunmuyor.
/// </summary>
public static class RdpFileImporter
{
    private static readonly ILogger Logger = Log.ForContext(typeof(RdpFileImporter));

    public static RdpConnection Import(string filePath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadAllLines(filePath))
        {
            var parts = line.Split(':', 3);
            if (parts.Length != 3) continue;
            values[parts[0].Trim()] = parts[2];
        }

        var connection = new RdpConnection
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            UserName = Get(values, "username") ?? string.Empty,
            Domain = Get(values, "domain") ?? string.Empty,
            ColorDepth = GetInt(values, "session bpp") ?? 32,
            AudioRedirectionMode = GetInt(values, "audiomode") ?? 0,
            RedirectPrinters = GetBool(values, "redirectprinters"),
            RedirectPorts = GetBool(values, "redirectcomports"),
            RedirectSmartCards = GetBool(values, "redirectsmartcards"),
            RedirectDrives = !string.IsNullOrWhiteSpace(Get(values, "drivestoredirect")),
            SmartSizing = GetBool(values, "smart sizing"),
            DisplayConnectionBar = GetBool(values, "displayconnectionbar"),
            EnableCredSsp = GetInt(values, "enablecredsspsupport") != 0,
            EnableAutoReconnect = GetInt(values, "autoreconnection enabled") != 0,
            AuthenticationLevel = GetInt(values, "authentication level") ?? 0,
            DesktopScaleFactor = GetInt(values, "desktopscalefactor") ?? 0,
            // gatewayhostname cogu .rdp'de bos degil ama kullanilmiyor; yalniz 1/2 (her zaman/algila) gecerli.
            GatewayHostname = GetInt(values, "gatewayusagemethod") is 1 or 2
                ? Get(values, "gatewayhostname") ?? string.Empty
                : string.Empty,
        };

        ApplyAddress(connection, Get(values, "full address"));

        // "DOMAIN\kullanici" formatini ayikla.
        var slash = connection.UserName.IndexOf('\\');
        if (slash > 0 && string.IsNullOrEmpty(connection.Domain))
        {
            connection.Domain = connection.UserName[..slash];
            connection.UserName = connection.UserName[(slash + 1)..];
        }

        Logger.Information("Ice aktarildi: {File} -> {Address}", Path.GetFileName(filePath), connection.Address);
        return connection;
    }

    private static void ApplyAddress(RdpConnection connection, string? fullAddress)
    {
        if (string.IsNullOrWhiteSpace(fullAddress)) return;

        fullAddress = fullAddress.Trim();

        // IPv6 koseli parantez icinde olabilir: [::1]:3389
        var separator = fullAddress.LastIndexOf(':');
        var closingBracket = fullAddress.LastIndexOf(']');

        if (separator > closingBracket && separator > 0 &&
            int.TryParse(fullAddress[(separator + 1)..], out var port))
        {
            connection.Address = fullAddress[..separator];
            connection.Port = port;
        }
        else
        {
            connection.Address = fullAddress;
        }

        if (string.IsNullOrWhiteSpace(connection.Name)) connection.Name = connection.Address;
    }

    private static string? Get(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value.Trim() : null;

    private static int? GetInt(Dictionary<string, string> values, string key)
        => int.TryParse(Get(values, key), out var result) ? result : null;

    private static bool GetBool(Dictionary<string, string> values, string key)
        => GetInt(values, key) == 1;
}
