using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace RdpManager.Views;

/// <summary>
/// Oturum kucuk resmini dosyadan yukler. Girdiler: [0]=yol, [1]=surum
/// (surum degisince yeniden okunur). Dosya yoksa null (Image gizlenir).
/// </summary>
public sealed class ThumbnailConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not string path || !File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Metin bossa Visible, doluysa Collapsed. Placeholder metinleri icin.</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true ise Visible, degilse Collapsed.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Tum girdiler true ise Visible, aksi halde Collapsed.</summary>
public sealed class AllTrueToVisibleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Sekme baslik rengi: sekme SECILIYSE zemin rengine gore kontrast (siyah/beyaz),
/// degilse verilen varsayilan firca (DragablzItem.Foreground — hover vb. korunur).
/// Girdiler: [0]=IsSelected (bool), [1]=Color (hex string?), [2]=varsayilan Brush.
/// </summary>
public sealed class TabTitleForegroundConverter : IMultiValueConverter
{
    private static readonly ContrastForegroundConverter Contrast = new();

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = values.Length > 0 && values[0] is true;
        if (!isSelected)
            return values.Length > 2 ? values[2] : null;

        var color = values.Length > 1 ? values[1] as string : null;
        return Contrast.Convert(color, targetType, parameter, culture);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Bir renk (hex string) zemin uzerinde okunakli metin rengi dondurur:
/// acik zeminde siyah, koyu zeminde beyaz. Renk yoksa (null) beyaz —
/// varsayilan zemin kirmizi vurgu rengi oldugu icin.
/// </summary>
public sealed class ContrastForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Black = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush White = new(Color.FromRgb(0xFF, 0xFF, 0xFF));

    static ContrastForegroundConverter()
    {
        Black.Freeze();
        White.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return White;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            // Algilanan parlaklik (ITU-R BT.601): esige gore siyah/beyaz.
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance > 0.6 ? Black : White;
        }
        catch
        {
            return White;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
