using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RdpManager.Models;
using Serilog;

namespace RdpManager.Services;

/// <summary>
/// Coklu monitor tam ekran icin mstsc.exe'ye devreder.
///
/// Neden: gomulu RDP ActiveX kontrolu, monitorlerin DPI olcekleri farkliysa
/// tam ekrani dogru cizemiyor. mstsc.exe bunu beceriyor.
///
/// Bedeli: oturum sekmeli kabuktan cikar, ayri bir mstsc penceresi olur.
/// Ayrica RD Gateway mstsc moduyla birlikte calismaz.
/// </summary>
public sealed class MstscLauncher
{
    private static readonly ILogger Logger = Log.ForContext<MstscLauncher>();

    /// <summary>mstsc dosyayi okuduktan sonra silmek icin beklenen sure.</summary>
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(20);

    public void Launch(RdpConnection connection, bool useMultimon)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdpmanager-{Guid.NewGuid():N}.rdp");
        File.WriteAllText(path, BuildRdpFile(connection), Encoding.Unicode);

        var arguments = useMultimon ? $"\"{path}\" /multimon" : $"\"{path}\"";

        try
        {
            Process.Start(new ProcessStartInfo("mstsc.exe", arguments) { UseShellExecute = true });
            Logger.Information("mstsc baslatildi: {Conn} (multimon={Multimon})",
                connection.DisplayName, useMultimon);
        }
        finally
        {
            ScheduleCleanup(path);
        }
    }

    private static void ScheduleCleanup(string path)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(CleanupDelay);
            try { File.Delete(path); }
            catch (Exception ex) { Logger.Debug(ex, "Gecici .rdp silinemedi: {Path}", path); }
        });
    }

    private static string BuildRdpFile(RdpConnection c)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"full address:s:{c.Address}:{c.Port}");
        builder.AppendLine($"username:s:{(string.IsNullOrEmpty(c.Domain) ? c.UserName : $"{c.Domain}\\{c.UserName}")}");
        builder.AppendLine($"session bpp:i:{c.ColorDepth}");
        builder.AppendLine($"audiomode:i:{c.AudioRedirectionMode}");
        builder.AppendLine($"redirectprinters:i:{(c.RedirectPrinters ? 1 : 0)}");
        builder.AppendLine($"redirectcomports:i:{(c.RedirectPorts ? 1 : 0)}");
        builder.AppendLine($"redirectsmartcards:i:{(c.RedirectSmartCards ? 1 : 0)}");
        builder.AppendLine($"drivestoredirect:s:{(c.RedirectDrives ? "*" : string.Empty)}");
        builder.AppendLine($"redirectclipboard:i:{(c.RedirectClipboard ? 1 : 0)}");
        builder.AppendLine($"camerastoredirect:s:{(c.RedirectCameras ? "*" : string.Empty)}");
        builder.AppendLine($"authentication level:i:{c.AuthenticationLevel}");
        builder.AppendLine($"enablecredsspsupport:i:{(c.EnableCredSsp ? 1 : 0)}");
        builder.AppendLine($"autoreconnection enabled:i:{(c.EnableAutoReconnect ? 1 : 0)}");
        builder.AppendLine("screen mode id:i:2");        // tam ekran
        builder.AppendLine("smart sizing:i:0");
        builder.AppendLine("dynamic resolution:i:1");

        if (c.DesktopScaleFactor > 0)
            builder.AppendLine($"desktopscalefactor:i:{c.DesktopScaleFactor}");

        if (!string.IsNullOrWhiteSpace(c.GatewayHostname))
        {
            builder.AppendLine($"gatewayhostname:s:{c.GatewayHostname.Trim()}");
            builder.AppendLine("gatewayusagemethod:i:1");
            builder.AppendLine("gatewayprofileusagemethod:i:1");
            builder.AppendLine("gatewaycredentialssource:i:0");
            builder.AppendLine("promptcredentialonce:i:1");
        }

        var encrypted = EncryptPasswordForMstsc(c.Password);
        if (encrypted is not null) builder.AppendLine($"password 51:b:{encrypted}");

        return builder.ToString();
    }

    /// <summary>
    /// mstsc, .rdp icindeki parolayi entropisiz DPAPI blob'unun HEX'i olarak bekler.
    /// Ayni Windows kullanicisinda gecerli — bu yuzden mstsc yeniden parola sormaz.
    /// Dosya baska bir makineye kopyalanirsa blob ise yaramaz.
    /// </summary>
    private static string? EncryptPasswordForMstsc(string? password)
    {
        if (string.IsNullOrEmpty(password)) return null;

        try
        {
            // mstsc parolayi UTF-16LE bekliyor.
            var bytes = Encoding.Unicode.GetBytes(password);
            try
            {
                var blob = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToHexString(blob);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "mstsc icin parola sifrelenemedi - mstsc soracak");
            return null;
        }
    }
}
