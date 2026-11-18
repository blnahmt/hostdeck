using System.Runtime.InteropServices;

namespace RdpManager.Rdp;

internal static class NativeMethods
{
    // --- Klavye enjeksiyonu (Ctrl+Alt+Del icin) ---

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12;   // Alt
    private const byte VK_END = 0x23;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, nuint dwExtraInfo);

    /// <summary>
    /// Odaklanmis RDP oturumuna Ctrl+Alt+End gonderir.
    /// RDP bunu oturum icinde Ctrl+Alt+Del olarak yorumlar — gercek Ctrl+Alt+Del
    /// isletim sistemi tarafindan yakalandigi icin uzak makineye hic ulasmaz.
    /// </summary>
    public static void SendCtrlAltEnd()
    {
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_MENU, 0, 0, 0);
        keybd_event(VK_END, 0, 0, 0);
        keybd_event(VK_END, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }

    private const byte VK_LWIN = 0x5B;
    private const byte VK_OEM_PERIOD = 0xBE;

    /// <summary>Windows emoji panelini acar (Win + .). Odaklanmis metin kutusuna ekler.</summary>
    public static void OpenEmojiPanel()
    {
        keybd_event(VK_LWIN, 0, 0, 0);
        keybd_event(VK_OEM_PERIOD, 0, 0, 0);
        keybd_event(VK_OEM_PERIOD, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
    }

    // --- Gorev cubugu bildirimi ---

    private const uint FLASHW_TRAY = 0x00000002;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    /// <summary>
    /// Pencerenin gorev cubugu dugmesini yanip sondurur.
    /// Arka plandaki bir oturum koptugunda kullanici baska uygulamadayken de fark etsin diye.
    /// FLASHW_TIMERNOFG: pencere one gelene kadar yanip soner.
    /// </summary>
    public static void FlashTaskbar(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = windowHandle,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };

        FlashWindowEx(ref info);
    }

    // --- Monitor DPI ---

    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    /// <summary>
    /// Imlecin ekran koordinati.
    ///
    /// Neden gerekli: tam ekran oturumunda fare RDP ActiveX kontrolunde oldugu
    /// icin WPF hicbir fare olayi gormez. Ust kenari yakalamanin tek yolu
    /// imleci disaridan yoklamak.
    /// </summary>
    public static (int X, int Y) GetCursorPosition()
        => GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // --- Pencere yakalama (oturum ekran goruntusu) ---

    /// <summary>Tum icerigi cizer (DirectComposition/DWM dahil) — Win8.1+.</summary>
    private const int PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    /// <summary>
    /// Verilen pencerenin (RDP ActiveX) icerigini bir Bitmap'e cizer.
    /// PW_RENDERFULLCONTENT sayesinde donanim/DX yuzeyleri de yakalanabilir.
    /// Basarisiz olursa null doner.
    /// </summary>
    public static System.Drawing.Bitmap? CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (!GetClientRect(hwnd, out var rect)) return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 2 || height < 2) return null;

        var bitmap = new System.Drawing.Bitmap(width, height);
        try
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Verilen ekran koordinatindaki monitorun efektif DPI'i. Basarisiz olursa 96 (=%100).
    /// </summary>
    public static uint GetMonitorDpi(int x, int y)
    {
        try
        {
            var handle = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (handle != IntPtr.Zero && GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
                return dpiX;
        }
        catch (DllNotFoundException)
        {
            // shcore.dll Windows 8.1 oncesinde yok.
        }

        return 96;
    }
}
