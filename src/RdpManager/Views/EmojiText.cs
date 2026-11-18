using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace RdpManager.Views;

/// <summary>
/// Renkli emoji gosteren kucuk kontrol. Emoji, gomulu Twemoji PNG'sinden
/// (<see cref="EmojiCatalog"/>) yuklenip kare olarak cizilir. WPF metin yolu
/// bu ortamda emojiyi renkli cizemedigi icin gorsel kullaniliyor.
/// </summary>
public sealed class EmojiText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmojiText),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmojiSizeProperty = DependencyProperty.Register(
        nameof(EmojiSize), typeof(double), typeof(EmojiText),
        new FrameworkPropertyMetadata(16.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double EmojiSize
    {
        get => (double)GetValue(EmojiSizeProperty);
        set => SetValue(EmojiSizeProperty, value);
    }

    private BitmapImage? Image => EmojiCatalog.Image(Text);

    protected override Size MeasureOverride(Size availableSize)
        => Image is null ? new Size(0, 0) : new Size(EmojiSize, EmojiSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var image = Image;
        if (image is null) return;

        // Kare cizim; Twemoji gorselleri 72x72 kare.
        drawingContext.DrawImage(image, new Rect(0, 0, EmojiSize, EmojiSize));
    }
}
