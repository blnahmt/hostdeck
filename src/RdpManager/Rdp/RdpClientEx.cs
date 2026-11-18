using System.Windows.Forms;
using AxMSTSCLib;

namespace RdpManager.Rdp;

/// <summary>
/// mstscax.dll ActiveX kontrolunun WPF icinde barinabilmesi icin gereken
/// WndProc duzeltmelerini iceren tureyen sinif.
///
/// Bu iki mesaj ele alinmazsa:
///   - WM_GETOBJECT      : ekran okuyucu / UI Automation aktifken kontrol cokuyor
///   - WM_MOUSEACTIVATE  : WPF <-> WinForms sinirinda ilk tikta odak oturuma gecmiyor
/// </summary>
internal sealed class RdpClientEx : AxMsRdpClient9NotSafeForScripting
{
    private const int WM_GETOBJECT = 0x003D;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_ACTIVATE = 1;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_GETOBJECT:
                // Yanit vermiyoruz: UI Automation agacina hic girmesin.
                m.Result = IntPtr.Zero;
                return;

            // WM_MOUSEACTIVATE'e HIC dokunulmuyor: AxHost'un kendi islemesi
            // (OLE UI-activation) suruklemenin calismasi icin sart.
        }

        base.WndProc(ref m);
    }
}
