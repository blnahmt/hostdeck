using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace RdpManager.Services;

/// <summary>Tray menusu renkleri (uygulamanin o anki temasindan okunur).</summary>
public sealed record TrayMenuPalette(
    Drawing.Color Background, Drawing.Color Text, Drawing.Color Muted, Drawing.Color Hover, Drawing.Color Border);

/// <summary>
/// WinForms tray menusunu uygulama temasiyla cizer: duz renkler, koseli kenarlar,
/// secili satirda tema "hover" zemini. Varsayilan beyaz Windows menusunun yerine.
/// </summary>
public sealed class TrayMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    private readonly TrayMenuPalette _palette;

    public TrayMenuRenderer(TrayMenuPalette palette) : base(new Colors(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    /// <summary>Secili satiri kendimiz ciziyoruz: renk tablosuna ragmen sistem mavisi cikiyordu.</summary>
    protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return; // zemini ToolStripDropDownBackground boyuyor
        using var brush = new Drawing.SolidBrush(_palette.Hover);
        e.Graphics.FillRectangle(brush, new Drawing.Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
    }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        // Basliklar (Favoriler, Son kullanilanlar) devre disi oge: soluk ama okunur.
        e.TextColor = e.Item.Enabled ? _palette.Text : _palette.Muted;
        base.OnRenderItemText(e);
    }

    private sealed class Colors(TrayMenuPalette p) : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => p.Background;
        public override Drawing.Color MenuBorder => p.Border;
        public override Drawing.Color MenuItemBorder => p.Hover;
        public override Drawing.Color MenuItemSelected => p.Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => p.Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => p.Hover;
        public override Drawing.Color ImageMarginGradientBegin => p.Background;
        public override Drawing.Color ImageMarginGradientMiddle => p.Background;
        public override Drawing.Color ImageMarginGradientEnd => p.Background;
        public override Drawing.Color SeparatorDark => p.Border;
        public override Drawing.Color SeparatorLight => p.Background;
    }
}
