using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace RdpManager.Security;

/// <summary>
/// Parolalari Windows DPAPI ile korur.
///
/// Neden DPAPI: anahtar Windows kullanicisina bagli. data.db baska bir kullaniciya
/// veya baska bir makineye kopyalanirsa parolalar COZULEMEZ. 1Remote'un uygulamaya
/// gomulu sabit anahtarli AES'inden bu yuzden daha guvenli.
///
/// Sinir: kullanici kendi oturumu acikken calisan her sey parolalari cozebilir.
/// Master password (Argon2id + AES-GCM) gerekirse ayri bir kademe olarak eklenecek.
/// </summary>
public sealed class CredentialProtector
{
    private static readonly ILogger Logger = Log.ForContext<CredentialProtector>();

    /// <summary>DPAPI blob'unu bu uygulamaya baglayan ek entropi.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RdpManager.v1.credential");

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var blob = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(blob);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return null;

        try
        {
            var blob = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }
        catch (Exception ex)
        {
            // Baska kullanici/makine, veya bozuk kayit. Parolasiz devam et.
            Logger.Warning(ex, "Parola cozulemedi - baska bir Windows kullanicisinda kaydedilmis olabilir");
            return null;
        }
    }
}
