using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

using Application = System.Windows.Application;

namespace RdpManager.Views;

/// <summary>
/// Gomulu Twemoji (renkli) emoji gorsellerini yonetir.
/// Emoji karakter dizisi -> Twemoji dosya adi (FE0F ayiklanir) -> gomulu PNG.
/// </summary>
internal static class EmojiCatalog
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new();
    private static IReadOnlyList<string>? _all;

    /// <summary>Secicide gosterilen kuratorlu emoji listesi (gomulu dosyadan).</summary>
    public static IReadOnlyList<string> All => _all ??= LoadList();

    /// <summary>Twemoji dosya adi: kod noktalari (0xFE0F haric) '-' ile birlesik, kucuk hex.</summary>
    public static string FileName(string emoji)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < emoji.Length;)
        {
            var cp = char.ConvertToUtf32(emoji, i);
            i += char.IsHighSurrogate(emoji[i]) ? 2 : 1;
            if (cp == 0xFE0F) continue;   // Twemoji varyasyon secicisini ayiklar
            if (sb.Length > 0) sb.Append('-');
            sb.Append(cp.ToString("x"));
        }
        return sb.ToString();
    }

    /// <summary>Emoji icin renkli gorseli dondurur; yoksa null.</summary>
    public static BitmapImage? Image(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return null;

        return Cache.GetOrAdd(emoji, e =>
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Emoji/{FileName(e)}.png");
                var info = Application.GetResourceStream(uri);
                if (info is null) return null;

                using var stream = info.Stream;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                // Fluent 3D gorselleri ~256px; simge boyutlarinda gosterildigi icin
                // belek tasarrufu adina daha kucuk cozunurlukte cozuyoruz.
                bmp.DecodePixelWidth = 64;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        });
    }

    private static IReadOnlyList<string> LoadList()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/Emoji/emoji-list.txt");
            var info = Application.GetResourceStream(uri);
            if (info is not null)
            {
                using var reader = new StreamReader(info.Stream, Encoding.UTF8);
                return reader.ReadToEnd()
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList();
            }
        }
        catch
        {
            // gomulu liste okunamadi
        }

        return ["🖥️"];
    }
}
