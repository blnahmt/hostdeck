using System.Windows.Input;

namespace RdpManager.Rdp;

/// <summary>
/// WPF tuslarini X11 keysym'lerine cevirir (RFB KeyEvent bunlari bekler).
/// Karakter ureten tuslar TextInput uzerinden <see cref="FromChar"/> ile gonderilir;
/// boylece Turkce/AltGr gibi klavye duzenleri Windows'un kendi cevirisiyle dogru gelir.
/// </summary>
public static class VncKeyMap
{
    public const uint ControlL = 0xFFE3, ControlR = 0xFFE4, AltL = 0xFFE9, AltR = 0xFFEA, Delete = 0xFFFF;

    private static readonly Dictionary<Key, uint> Special = new()
    {
        [Key.Back] = 0xFF08, [Key.Tab] = 0xFF09, [Key.Enter] = 0xFF0D, [Key.Escape] = 0xFF1B,
        [Key.Delete] = Delete, [Key.Insert] = 0xFF63, [Key.Home] = 0xFF50, [Key.End] = 0xFF57,
        [Key.PageUp] = 0xFF55, [Key.PageDown] = 0xFF56,
        [Key.Left] = 0xFF51, [Key.Up] = 0xFF52, [Key.Right] = 0xFF53, [Key.Down] = 0xFF54,
        [Key.Pause] = 0xFF13, [Key.Scroll] = 0xFF14, [Key.PrintScreen] = 0xFF61, [Key.Apps] = 0xFF67,
        [Key.NumLock] = 0xFF7F, [Key.CapsLock] = 0xFFE5,
        [Key.LeftShift] = 0xFFE1, [Key.RightShift] = 0xFFE2,
        [Key.LeftCtrl] = ControlL, [Key.RightCtrl] = ControlR,
        [Key.LeftAlt] = AltL, [Key.RightAlt] = AltR,
        [Key.LWin] = 0xFFEB, [Key.RWin] = 0xFFEC,
        [Key.F1] = 0xFFBE, [Key.F2] = 0xFFBF, [Key.F3] = 0xFFC0, [Key.F4] = 0xFFC1,
        [Key.F5] = 0xFFC2, [Key.F6] = 0xFFC3, [Key.F7] = 0xFFC4, [Key.F8] = 0xFFC5,
        [Key.F9] = 0xFFC6, [Key.F10] = 0xFFC7, [Key.F11] = 0xFFC8, [Key.F12] = 0xFFC9,
    };

    /// <summary>Karakter uretmeyen (veya kontrol karakteri ureten) tuslar.</summary>
    public static bool TryGetSpecial(Key key, out uint keysym) => Special.TryGetValue(key, out keysym);

    /// <summary>Ctrl/Alt kisayollari (Ctrl+C gibi): TextInput kontrol karakteri urettiginden harf/rakam dogrudan eslenir.</summary>
    public static bool TryGetShortcut(Key key, out uint keysym)
    {
        keysym = key switch
        {
            >= Key.A and <= Key.Z => (uint)('a' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
            Key.Space => ' ',
            _ => 0,
        };
        return keysym != 0;
    }

    /// <summary>
    /// Tus, su anki duzen ve basili degistiricilerle yazdirilabilir karakter uretiyor mu?
    /// Ctrl+Alt (AltGr) ile @, €, { gibi karakterler TextInput'a birakilmali; Ctrl+C gibi
    /// kontrol karakteri ureten kombinasyonlar ise kisayol olarak gonderilir.
    /// </summary>
    public static bool ProducesText(Key key)
    {
        var state = new byte[256];
        if (!GetKeyboardState(state)) return false;
        var buffer = new char[4];
        // 0x4: cekirdek klavye durumunu degistirme (olu tuslar bozulmasin).
        var count = ToUnicodeEx((uint)KeyInterop.VirtualKeyFromKey(key), 0, state, buffer, buffer.Length, 0x4, GetKeyboardLayout(0));
        return count > 0 && !char.IsControl(buffer[0]);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint vk, uint scanCode, byte[] state,
        [System.Runtime.InteropServices.Out] char[] buffer, int bufferSize, uint flags, IntPtr layout);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    /// <summary>Latin-1 karakterler keysym ile ayni kodu kullanir; digerleri 0x01000000 + Unicode.</summary>
    public static uint FromChar(char c) => c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ'
        ? c
        : 0x01000000u | c;
}
