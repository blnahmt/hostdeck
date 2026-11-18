using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Markup;

namespace RdpManager.Localization;

/// <summary>
/// Arayuz dili. Turkce metnin kendisi anahtardir: Turkce'de oldugu gibi doner,
/// Ingilizce'de Localization/en/*.tsv sozlugunden karsiligi aranir; bulunamazsa
/// Turkce gosterilir (eksik ceviri arayuzu bozmaz).
///
/// Dil baslangicta bir kez secilir; degisiklik yeniden baslatinca uygulanir
/// (pencereler acikken metinleri yeniden kurmak yerine).
/// </summary>
public static class Loc
{
    public const string SettingKey = "ui.language";

    private static Dictionary<string, string> _english = [];

    public static bool IsEnglish { get; private set; }

    /// <summary>"tr" veya "en". Bos/gecersizse surumun varsayilani kullanilir.</summary>
    public static string Language => IsEnglish ? "en" : "tr";

    public static void Initialize(string? language)
    {
        language = language is "tr" or "en" ? language : DefaultLanguage();
        IsEnglish = language == "en";
        if (IsEnglish) _english = LoadEnglish();
    }

    /// <summary>Dis surum Windows diline uyar; ic surum Turkce baslar.</summary>
    public static string DefaultLanguage() => AppEdition.FollowsSystemLanguage
        && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr" ? "en" : "tr";

    public static string T(string turkish)
        => IsEnglish && _english.TryGetValue(turkish, out var english) ? english : turkish;

    /// <summary>Bicimli metin: <c>Loc.F("{0} oturum acik", n)</c>.</summary>
    public static string F(string turkish, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(turkish), args);

    /// <summary>Gomulu tum en/*.tsv dosyalarini birlestirir. Satir: Turkce&lt;TAB&gt;Ingilizce, \n satir sonu.</summary>
    public static Dictionary<string, string> LoadEnglish()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Localization.en.", StringComparison.Ordinal) && n.EndsWith(".tsv", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            foreach (var (tr, en) in Parse(reader.ReadToEnd())) result[tr] = en;
        }
        return result;
    }

    public static IEnumerable<(string Turkish, string English)> Parse(string tsv)
    {
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var tab = line.IndexOf('\t');
            // Yorum: '#' ile baslayip sekme icermeyen satir ("#{0} ..." gibi anahtarlar yorum degil).
            if (line.Length == 0 || (line.StartsWith('#') && tab < 0)) continue;
            if (tab <= 0 || tab == line.Length - 1) continue;
            yield return (Unescape(line[..tab]), Unescape(line[(tab + 1)..]));
        }
    }

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");
}

/// <summary>XAML: <c>Text="{loc:T 'Yeni baglanti'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string text) => Text = text;

    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
