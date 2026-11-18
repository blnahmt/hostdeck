using System.ComponentModel;
using System.Diagnostics;
using RdpManager.Localization;
using RdpManager.Models;

namespace RdpManager.Services;

/// <summary>
/// SSH baglantilarini Windows'un kendi ssh.exe'siyle acar.
/// Windows Terminal varsa hepsi tek bir pencerede sekme olarak toplanir;
/// yoksa klasik konsol penceresi acilir.
///
/// shortcut: terminal uygulamanin kendi sekmesinde degil Windows Terminal'de aciliyor;
/// gomulu terminal istenirse Microsoft.Terminal.Wpf + ConPTY ile SessionTabItem'a tasinmali.
/// </summary>
public static class SshLauncher
{
    public static void Open(RdpConnection c)
    {
        var host = c.Address.Trim();
        var user = c.UserName.Trim();

        // Kayitlar ice aktarilan dosyadan gelebilir: arguman olarak ssh'a giden degerleri dogrula.
        if (!IsSafeArgument(host) || (user.Length > 0 && !IsSafeArgument(user)))
            throw new InvalidOperationException(
                Loc.T("Adres veya kullanici adi bosluk, ; veya tirnak iceriyor ya da '-' ile basliyor."));

        string[] ssh = ["ssh", "-p", c.Port.ToString(), user.Length > 0 ? $"{user}@{host}" : host];

        try
        {
            // ';' Windows Terminal'de komut ayirici; basliktan cikariliyor.
            Start("wt.exe", ["-w", AppEdition.Id, "new-tab", "--title", c.DisplayName.Replace(';', ','),
                "--suppressApplicationTitle", .. ssh]);
        }
        catch (Win32Exception)
        {
            Start("conhost.exe", ssh); // Windows Terminal yuklu degil
        }
    }

    /// <summary>
    /// '-' ile baslayan deger ssh secenegi sayilir (or. -oProxyCommand=... komut calistirir);
    /// bosluk/; /tirnak arguman sinirini bozar.
    /// </summary>
    public static bool IsSafeArgument(string value)
        => value.Length > 0 && value[0] != '-'
           && value.All(ch => !char.IsWhiteSpace(ch) && ch is not (';' or '"' or '\'' or '&' or '|'));

    private static void Start(string fileName, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }
}
