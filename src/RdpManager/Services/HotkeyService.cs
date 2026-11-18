using System.Runtime.InteropServices;
using System.Windows.Interop;
using Serilog;

namespace RdpManager.Services;

/// <summary>
/// Uygulama arka plandayken bile calisan genel kisayol (varsayilan Alt+M).
///
/// Mesajlari almak icin gorunmez bir "message-only" pencere kullaniliyor;
/// boylece kisayol, uygulamanin hicbir penceresi acik/odakta olmasa da calisir.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<HotkeyService>();

    private const int HotkeyId = 0xA17;
    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_M = 0x4D;

    /// <summary>
    /// Sirayla denenir; ilk kaydedilebilen kullanilir.
    /// Alt+M yaygin bir kisayol, baska uygulamalar tarafindan alinmis olabiliyor.
    /// </summary>
    private static readonly (uint Modifiers, uint Key, string Text)[] Candidates =
    [
        (MOD_ALT, VK_M, "Alt+M"),
        (MOD_CONTROL | MOD_ALT, VK_M, "Ctrl+Alt+M"),
        (MOD_CONTROL | MOD_SHIFT, VK_M, "Ctrl+Shift+M"),
    ];

    /// <summary>Pencere hiyerarsisine girmeyen, yalniz mesaj alan pencere.</summary>
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Pressed;

    /// <summary>Kaydedilebilen kisayolun okunabilir hali, yoksa null.</summary>
    public string? ActiveHotkey { get; private set; }

    public void Register()
    {
        if (_source is not null) return;

        _source = new HwndSource(new HwndSourceParameters("RdpManagerHotkey")
        {
            ParentWindow = HWND_MESSAGE,
        });
        _source.AddHook(Hook);

        foreach (var (modifiers, key, text) in Candidates)
        {
            if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | MOD_NOREPEAT, key)) continue;

            _registered = true;
            ActiveHotkey = text;
            Logger.Information("Genel kisayol kaydedildi: {Hotkey}", text);
            return;
        }

        Logger.Warning("Hicbir genel kisayol kaydedilemedi - hepsi baska uygulamalarda");
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;

        handled = true;
        Pressed?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null) return;

        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(Hook);
        _source.Dispose();
        _source = null;
    }
}
