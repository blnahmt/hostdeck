using System.ComponentModel;
using System.Text.Json.Serialization;
using RdpManager.Localization;

namespace RdpManager.Models;

/// <summary>
/// Bir RDP baglantisinin tanimi.
/// Nesnenin tamami tek bir JSON sutununa serialize ediliyor; yeni alan eklemek
/// migration gerektirmez, sadece <see cref="SchemaVersion"/> artar.
/// </summary>
public sealed class RdpConnection : INotifyPropertyChanged
{
    public const int CurrentSchemaVersion = 2;

    private bool _isSessionOpen;
    private string? _remoteUser;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = string.Empty;

    /// <summary>Serbest etiketler. Gruplamanin yerini aldi; bir baglantida birden fazla olabilir.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Listede solda gosterilen renk serigi. #RRGGBB veya null.</summary>
    public string? Color { get; set; }

    /// <summary>Sunucu simgesi (emoji). Varsayilan bilgisayar simgesi.</summary>
    public string Icon { get; set; } = "🖥️";

    /// <summary>Sabitlenmis (favori) baglanti — listenin en ustunde gruplanir.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Elle siralamada konum. Kucukten buyuge.</summary>
    public int SortOrder { get; set; }

    /// <summary>Son basarili baglanti (unix saniye). "Son kullanim" siralamasi icin.</summary>
    public long? LastConnectedAt { get; set; }

    /// <summary>"RDP", "VNC" veya "SSH". SSH baglantilari Windows Terminal'de acilir.</summary>
    public string Protocol { get; set; } = "RDP";

    [JsonIgnore]
    public bool IsSsh => string.Equals(Protocol, "SSH", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsVnc => string.Equals(Protocol, "VNC", StringComparison.OrdinalIgnoreCase);

    /// <summary>Protokolun varsayilan portu (editor protokol degisince kullanir).</summary>
    public static int DefaultPort(string protocol) => protocol.ToUpperInvariant() switch
    {
        "SSH" => 22,
        "VNC" => 5900,
        _ => 3389,
    };

    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;

    /// <summary>RD Gateway sunucusu. Bossa dogrudan baglanilir; gateway ayni kimlik bilgilerini kullanir.</summary>
    public string GatewayHostname { get; set; } = string.Empty;

    public string UserName { get; set; } = "administrator";
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Duz metin parola. ASLA diske yazilmaz (JsonIgnore).
    /// Editorden gelir veya baglanmadan hemen once cozulur.
    /// </summary>
    [JsonIgnore]
    public string? Password { get; set; }

    /// <summary>DPAPI ile korunmus parola (base64). Diske yazilan tek surum budur.</summary>
    public string? ProtectedPassword { get; set; }

    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(ProtectedPassword);

    // --- Goruntu ---
    public int ColorDepth { get; set; } = 32;
    public bool SmartSizing { get; set; } = true;
    public bool DisplayConnectionBar { get; set; }

    /// <summary>%100-%500 arasi. 0 = otomatik (monitorun DPI'ina gore).</summary>
    public int DesktopScaleFactor { get; set; }

    // --- Yonlendirmeler ---
    public bool RedirectDrives { get; set; }
    public bool RedirectPrinters { get; set; }
    public bool RedirectPorts { get; set; }
    public bool RedirectSmartCards { get; set; }
    public bool RedirectClipboard { get; set; } = true;

    /// <summary>Yerel kameralari uzak oturuma yonlendir (Windows 10 1803+ sunucu gerekir).</summary>
    public bool RedirectCameras { get; set; }

    /// <summary>0 = bu bilgisayarda cal, 1 = uzak bilgisayarda cal, 2 = calma.</summary>
    public int AudioRedirectionMode { get; set; }

    /// <summary>
    /// Windows tusu ve Win+Shift+S gibi kisayollar uzak oturuma gitsin mi.
    /// Kapaliyken (mstsc'nin varsayilani) yalniz tam ekranda uzak oturuma gider.
    /// </summary>
    public bool SendWindowsKeys { get; set; } = true;

    // --- Baglanti davranisi ---
    public bool EnableCredSsp { get; set; } = true;
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>0 = sunucu dogrulamasi yapma, 1 = basarisizsa baglanma, 2 = uyar.</summary>
    public int AuthenticationLevel { get; set; } = 0;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name;

    [JsonIgnore]
    public string TagsDisplay => string.Join("  ", Tags);

    [JsonIgnore]
    public bool HasTags => Tags.Count > 0;

    /// <summary>
    /// Bu baglantinin su anda acik bir oturumu var mi.
    /// Gecici durum — diske yazilmaz, sadece listede gostergeyi beslemek icin.
    /// </summary>
    [JsonIgnore]
    public bool IsSessionOpen
    {
        get => _isSessionOpen;
        set
        {
            if (_isSessionOpen == value) return;
            _isSessionOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSessionOpen)));
        }
    }

    /// <summary>
    /// API'den gelen: sunucuda su an bagli olan kullanici.
    /// null ise kimse bagli degil. Gecici durum — diske yazilmaz.
    /// </summary>
    [JsonIgnore]
    public string? RemoteUser
    {
        get => _remoteUser;
        set
        {
            if (_remoteUser == value) return;
            _remoteUser = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoteUser)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRemoteUser)));
        }
    }

    [JsonIgnore]
    public bool HasRemoteUser => !string.IsNullOrEmpty(_remoteUser);

    private bool? _isReachable;

    /// <summary>Son yoklamada port acik miydi. null = henuz yoklanmadi. Gecici durum.</summary>
    [JsonIgnore]
    public bool? IsReachable
    {
        get => _isReachable;
        set
        {
            if (_isReachable == value) return;
            _isReachable = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReachable)));
        }
    }

    /// <summary>
    /// Son oturum ekran goruntusu (kucuk resim). Diske resmin kendisi ayri
    /// dosyada tutulur; burada yalnizca yol/durum acuga cikar.
    /// ThumbnailVersion, dosya guncellenince listedeki resmin yeniden yuklenmesini tetikler.
    /// </summary>
    [JsonIgnore]
    public string ThumbnailPath => Services.ThumbnailStore.PathFor(Id);

    [JsonIgnore]
    public bool HasThumbnail => System.IO.File.Exists(ThumbnailPath);

    private int _thumbnailVersion;

    [JsonIgnore]
    public int ThumbnailVersion
    {
        get => _thumbnailVersion;
        set
        {
            _thumbnailVersion = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailVersion)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailPath)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RdpConnection Clone()
    {
        var copy = (RdpConnection)MemberwiseClone();
        copy.Tags = [.. Tags];   // liste referansi paylasilmasin

        // MemberwiseClone olay alanini da kopyalar; kopya orijinalin
        // abonelerini devralmasin diye temizliyoruz.
        copy.PropertyChanged = null;
        return copy;
    }

    /// <summary>Cogaltma icin: ayni ayarlar, yeni kimlik ve isim.</summary>
    public RdpConnection Duplicate()
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = Loc.F("{0} (kopya)", DisplayName);
        copy.SortOrder = 0;              // listenin sonuna eklensin
        copy.LastConnectedAt = null;
        copy.IsSessionOpen = false;
        // ProtectedPassword bilerek korunuyor: ayni Windows kullanicisinda cozulebilir.
        return copy;
    }
}
