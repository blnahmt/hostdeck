using RdpManager.Localization;

namespace RdpManager.Rdp;

/// <summary>
/// OnDisconnected olayindan gelen ham kodlarin insanca okunabilir hale getirilmis hali.
/// </summary>
public sealed record RdpDisconnectInfo(
    int Reason,
    int ExtendedReason,
    string Message,
    bool IsUserInitiated,
    bool IsNetworkFailure)
{
    public override string ToString() => $"{Message} (kod {Reason}/{ExtendedReason})";
}

internal static class RdpDisconnectReasons
{
    /// <summary>
    /// Sadece anlami kesin bilinen kodlar. Geri kalani icin kontrolun kendi
    /// GetErrorDescription() metodu kullaniliyor (Windows tarafindan yerellestirilmis
    /// ve otoriter kaynak) — bkz. RdpHost.Describe().
    /// </summary>
    private static readonly Dictionary<int, string> Known = new()
    {
        [0] = "Baglanti kesildi.",
        [1] = "Baglanti bu bilgisayardan kapatildi.",
        [2] = "Oturum uzak kullanici tarafindan kapatildi.",
        [3] = "Oturum sunucu tarafindan sonlandirildi.",
        [260] = "Sunucu adi cozumlenemedi (DNS hatasi). Adresi kontrol edin.",
        [264] = "Baglanti zaman asimina ugradi. Sunucu yanit vermiyor.",
        [516] = "Sunucuya ulasilamadi. Sunucu kapali olabilir veya port engellenmis olabilir.",
        [520] = "Sunucu bulunamadi. Adresi kontrol edin.",
        [772] = "Ag baglantisi koptu (veri gonderilemedi).",
        [2308] = "Baglanti beklenmedik sekilde koptu (soket kapandi). Ag baglantisini kontrol edin.",
        [2311] = "Sunucu baglantiyi sonlandirdi.",
        [3079] = "Lisans sunucusuna ulasilamadi (RDS lisanslama hatasi).",
    };

    /// <summary>
    /// Otomatik yeniden baglanmanin GUVENLI oldugu kodlar: yalnizca ag/soket
    /// seviyesindeki kopmalar.
    ///
    /// Bilerek DISARIDA birakilanlar:
    ///  - 1/2/3  : kullanici veya sunucu bilerek kapatti (baska biri baglandi,
    ///             oturum kapatildi, yonetici attı) — tekrar denemek yanlis olur
    ///  - 260/520: adres hatali, tekrar denemek duzeltmez
    ///  - 3079   : lisans sorunu, tekrar denemek duzeltmez
    ///  - kimlik dogrulama hatalari: parola yanlisken dongu olusur
    /// </summary>
    private static readonly HashSet<int> NetworkFailures = [264, 516, 772, 2308];

    public static bool TryDescribe(int reason, out string message)
    {
        // Ceviri arama aninda: Known statik baslatilirken dil henuz secilmemis olabilir.
        if (!Known.TryGetValue(reason, out var turkish)) { message = string.Empty; return false; }
        message = Loc.T(turkish);
        return true;
    }

    /// <summary>Kullanicinin kendi kapattigi oturumlar hata olarak gosterilmemeli.</summary>
    public static bool IsUserInitiated(int reason) => reason is 1 or 2 or 3;

    public static bool IsNetworkFailure(int reason) => NetworkFailures.Contains(reason);
}
