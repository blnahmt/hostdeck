using System.Runtime.InteropServices;

namespace RdpManager.Views;

/// <summary>
/// WindowStyle=None + WindowChrome ile buyutulen pencerelerin dogru
/// boyutlandirilmasi.
///
/// Sorun: bu modda Windows, pencereyi yeniden boyutlandirma kenarligi kadar
/// (her yandan ~8px) monitorden BUYUK yapar. Icerik bu kadar disari tasar.
/// Bunu XAML tarafinda Margin ile telafi etmek ise WindowsFormsHost icindeki
/// HWND'yi kaydiriyor ve uzak masaustu sekme seridinin altinda kaliyor.
///
/// Cozum: WM_GETMINMAXINFO'ya cevap verip buyutulmus boyutu monitorun
/// CALISMA ALANINA (gorev cubugu haric) tam esitlemek. Boylece tasma hic olmaz.
/// </summary>
internal static class ChromeInterop
{
    public const int WM_GETMINMAXINFO = 0x0024;

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    /// <summary>
    /// Buyutulmus boyutu, pencerenin bulundugu monitorun calisma alanina esitler.
    /// </summary>
    public static void ClampMaximizedToWorkArea(IntPtr windowHandle, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        // Konum ve boyut monitore GORE (monitorun sol ust kosesi = 0,0).
        info.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
        info.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
        info.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        info.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;

        Marshal.StructureToPtr(info, lParam, true);
    }
}
