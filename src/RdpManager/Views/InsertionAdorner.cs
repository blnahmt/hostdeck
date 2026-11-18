using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace RdpManager.Views;

/// <summary>
/// Surukle-birak sirasinda ogenin nereye eklenecegini gosteren yatay cizgi.
/// ListBox'in adorner katmanina cizilir; fare olayi yemesin diye hit-test kapali.
/// </summary>
public sealed class InsertionAdorner : Adorner
{
    private readonly Pen _pen;
    private readonly Brush _brush;

    public InsertionAdorner(UIElement adorned, Brush brush) : base(adorned)
    {
        IsHitTestVisible = false;
        _brush = brush;
        _pen = new Pen(brush, 2.5);
        _pen.Freeze();
    }

    /// <summary>Cizginin dikey konumu (adorned element koordinatinda).</summary>
    public double Y { get; set; }

    public double X1 { get; set; }
    public double X2 { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var left = new Point(X1, Y);
        var right = new Point(X2, Y);

        dc.DrawLine(_pen, left, right);
        // Uclarda kucuk daireler — ekleme noktasi net gorunsun.
        dc.DrawEllipse(_brush, null, left, 3.5, 3.5);
        dc.DrawEllipse(_brush, null, right, 3.5, 3.5);
    }
}
