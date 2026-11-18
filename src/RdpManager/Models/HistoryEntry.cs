using RdpManager.Localization;

namespace RdpManager.Models;

/// <summary>Bir baglanti gecmisi kaydi (History tablosu).</summary>
public sealed class HistoryEntry
{
    public long Id { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Protocol { get; set; } = "RDP";
    public long StartedAt { get; set; }

    /// <summary>null: oturum hala acik ya da (SSH) bitis bilinmiyor.</summary>
    public long? EndedAt { get; set; }

    public string StartedText =>
        DateTimeOffset.FromUnixTimeSeconds(StartedAt).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public string DurationText => EndedAt is { } end
        ? FormatDuration(end - StartedAt)
        : Protocol == "SSH" ? "SSH" : "-";

    public static string FormatDuration(long seconds) => seconds switch
    {
        < 60 => Loc.F("{0} sn", Math.Max(seconds, 0)),
        < 3600 => Loc.F("{0} dk", seconds / 60),
        _ => Loc.F("{0} sa {1} dk", seconds / 3600, seconds % 3600 / 60),
    };
}
