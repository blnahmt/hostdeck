using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Serilog;

using Application = System.Windows.Application;

namespace RdpManager.Services;

/// <summary>
/// Windows'un baslik cubugunu (non-client alan) koyu temaya cevirir.
///
/// WPF baslik cubugunu cizmez; onu DWM cizer ve uygulamanin temasini
/// bilmez. Bu oznitelik verilmezse koyu temada beyaz bir baslik cubugu kalir.
/// </summary>
public static class WindowTheming
{
    private static readonly ILogger Logger = Log.ForContext(typeof(WindowTheming));

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33; // Windows 11+, eskide sessizce yok sayilir
    private const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsDark { get; internal set; }

    /// <summary>
    /// Pencereyi izlemeye alir: handle olusunca ve tema degisince
    /// baslik cubugu guncellenir.
    /// </summary>
    public static void Track(Window window)
    {
        if (window.IsInitialized && new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
        else
            window.SourceInitialized += (_, _) => Apply(window);
    }

    public static void RefreshAll()
    {
        foreach (Window window in Application.Current?.Windows ?? new WindowCollection())
            Apply(window);
    }

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        try
        {
            var value = IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));

            // Windows 11 pencere koselerini kendisi yuvarliyor; arayuz koseli olmali.
            var corner = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch (Exception ex)
        {
            // Windows 10 1809 oncesinde bu oznitelik yok - olumcul degil.
            Logger.Debug(ex, "Baslik cubugu temasi uygulanamadi");
        }
    }
}
