using System.Security.Cryptography;
using System.Text;

namespace RdpManager.Security;

/// <summary>
/// Disa aktarilan dosyayi kullanicinin verdigi parolayla sifreler.
///
/// Neden DPAPI degil: DPAPI blob'u makineye ve Windows kullanicisina bagli,
/// baska bilgisayarda cozulemez — yedegi geri yuklemek ya da baska makineye
/// tasimak ise disa aktarmanin asil amaci. Parolayla turetilen anahtar
/// tasinabilir olmayi saglar.
///
/// PBKDF2-SHA256 (210k tur) + AES-256-GCM. GCM ayrica butunlugu de dogrular:
/// dosya kurcalanmissa cozme basarisiz olur.
/// </summary>
public static class PassphraseCipher
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <summary>OWASP'in PBKDF2-SHA256 icin onerdigi alt sinir.</summary>
    public const int Iterations = 210_000;

    public sealed record Envelope(string Salt, string Payload);

    public static Envelope Encrypt(string plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(passphrase, salt);

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag);
        }
        finally
        {
            Array.Clear(key);
            Array.Clear(plain);
        }

        // payload = nonce | tag | ciphertext
        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);

        return new Envelope(Convert.ToBase64String(salt), Convert.ToBase64String(payload));
    }

    /// <summary>
    /// Parola yanlissa veya dosya bozuksa <see cref="CryptographicException"/> atar.
    /// </summary>
    public static string Decrypt(Envelope envelope, string passphrase)
    {
        var salt = Convert.FromBase64String(envelope.Salt);
        var payload = Convert.FromBase64String(envelope.Payload);

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Dosya icerigi eksik veya bozuk.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);

        var key = DeriveKey(passphrase, salt);
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            Array.Clear(key);
            Array.Clear(plain);
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
