using System.Windows.Forms;
using RdpManager.Localization;
using RdpManager.Rdp;

namespace RdpManager.Services;

public sealed record MonitorInfo(System.Drawing.Rectangle Bounds, uint Dpi, bool IsPrimary)
{
    public int ScalePercent => (int)Math.Round(Dpi * 100.0 / 96.0);
}

public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();

        foreach (var screen in Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            // Monitorun merkezinden sor: kenar koordinatlari komsu monitore dusebiliyor.
            var dpi = NativeMethods.GetMonitorDpi(
                bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2);

            result.Add(new MonitorInfo(bounds, dpi, screen.Primary));
        }

        return result;
    }

    /// <summary>
    /// Monitorlerin DPI'lari farkli mi?
    ///
    /// Onemli: RDP ActiveX kontrolu karisik DPI'li coklu monitorde tam ekrani
    /// dogru cizemiyor (1Remote'un mstsc moduna kacmasinin sebebi bu).
    /// Bu durumda coklu monitor tam ekran icin mstsc.exe'ye devrediyoruz.
    /// </summary>
    public static bool HasMixedDpi()
    {
        var monitors = GetMonitors();
        return monitors.Select(m => m.Dpi).Distinct().Count() > 1;
    }

    public static string DescribeLayout()
    {
        var monitors = GetMonitors();
        return string.Join(", ", monitors.Select((m, i) =>
            Loc.F("#{0} {1}x{2} %{3}", i + 1, m.Bounds.Width, m.Bounds.Height, m.ScalePercent)));
    }
}
