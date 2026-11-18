using RdpManager.Localization;
using Xunit;

namespace RdpManager.Tests;

public class LocTests
{
    [Fact]
    public void Parses_tab_separated_pairs_and_skips_comments()
    {
        var pairs = Loc.Parse("# yorum\nYeni baglanti\tNew connection\r\n{0} oturum\\nacik\t{0} sessions\\nopen\nbozuk satir\n#{0} %{1}\t#{0} {1}%\n").ToList();

        Assert.Equal(3, pairs.Count);
        Assert.Equal(("Yeni baglanti", "New connection"), pairs[0]);
        Assert.Equal(("{0} oturum\nacik", "{0} sessions\nopen"), pairs[1]);
        Assert.Equal(("#{0} %{1}", "#{0} {1}%"), pairs[2]);
    }

    [Fact]
    public void Every_english_entry_keeps_format_placeholders()
    {
        // Ceviride {0} gibi yer tutucu eksik/fazla olursa string.Format calisma aninda patlar.
        foreach (var (tr, en) in Loc.LoadEnglish())
        {
            for (var i = 0; i < 6; i++)
                Assert.True(tr.Contains($"{{{i}") == en.Contains($"{{{i}"), $"Yer tutucu {{{i}}} uyusmuyor: '{tr}' -> '{en}'");
        }
    }
}
