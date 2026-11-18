using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Serilog;

namespace RdpManager.Services;

/// <summary>
/// Oturum kapanmadan once alinan son ekran goruntusunu (kucuk resim) diske yazar.
/// Baglanti kimligine gore saklanir: {DataDirectory}\thumbnails\{id}.png
/// </summary>
public static class ThumbnailStore
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ThumbnailStore));

    /// <summary>Kaydedilen kucuk resmin en genis kenari (piksel).</summary>
    private const int MaxWidth = 360;

    public static string Directory => Path.Combine(App.DataDirectory, "thumbnails");

    public static string PathFor(string id) => Path.Combine(Directory, id + ".png");

    /// <summary>
    /// Yakalanan bitmap'i olceklendirip PNG olarak kaydeder.
    /// Goruntu tekduze ( or. tamamen siyah = yakalama basarisiz) ise kaydetmez.
    /// </summary>
    public static bool Save(Bitmap source, string id)
    {
        try
        {
            if (IsBlank(source)) return false;

            System.IO.Directory.CreateDirectory(Directory);

            var scale = Math.Min(1.0, (double)MaxWidth / source.Width);
            var width = Math.Max(1, (int)(source.Width * scale));
            var height = Math.Max(1, (int)(source.Height * scale));

            using var thumb = new Bitmap(width, height);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, width, height);
            }

            // Once gecici dosyaya yaz, sonra tasi — okuma sirasinda yaricizik olmasin.
            var target = PathFor(id);
            var temp = target + ".tmp";
            thumb.Save(temp, ImageFormat.Png);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Kucuk resim kaydedilemedi: {Id}", id);
            return false;
        }
    }

    /// <summary>Tum kucuk resimleri siler.</summary>
    public static void Clear()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Kucuk resimler temizlenemedi");
        }
    }

    public static void Delete(string id)
    {
        try
        {
            var path = PathFor(id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Kucuk resim silinemedi: {Id}", id);
        }
    }

    /// <summary>Goruntu neredeyse tekduze mi? (basarisiz yakalama genelde dumduz siyah)</summary>
    private static bool IsBlank(Bitmap bmp)
    {
        // Bir izgara uzerinde ornekle; hepsi ayni renkse bos say.
        var first = bmp.GetPixel(0, 0);
        const int steps = 6;

        for (var i = 0; i <= steps; i++)
        {
            for (var j = 0; j <= steps; j++)
            {
                var x = Math.Min(bmp.Width - 1, i * bmp.Width / steps);
                var y = Math.Min(bmp.Height - 1, j * bmp.Height / steps);
                var p = bmp.GetPixel(x, y);
                if (p.R != first.R || p.G != first.G || p.B != first.B)
                    return false;
            }
        }

        return true;
    }
}
