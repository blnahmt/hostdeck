using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RdpManager.Localization;
using RdpManager.Models;
using RdpManager.Security;
using Serilog;

namespace RdpManager.Data;

/// <summary>
/// Baglanti listesinin JSON olarak disa/ice aktarimi.
///
/// Parolalar dosyaya duz metin olarak ASLA yazilmaz. Kullanici bir parola
/// verirse tum icerik onunla sifrelenir (AES-256-GCM); vermezse baglantilar
/// parolasiz aktarilir.
///
/// DPAPI blob'u disari yazilmiyor: o blob makineye ve Windows kullanicisina
/// bagli, baska bilgisayarda cozulemezdi. Bunun yerine parolalar cozulup
/// sifreli pakete konuyor, ice aktarilan makinede yeniden DPAPI ile korunuyor.
/// </summary>
public static class ConnectionJsonIO
{
    private const string FormatName = "rdpmanager-export";
    private const int FormatVersion = 1;

    private static readonly ILogger Logger = Log.ForContext(typeof(ConnectionJsonIO));

    private static readonly JsonSerializerOptions FileOptions = new(ConnectionRepository.JsonOptions)
    {
        WriteIndented = true,
    };

    /// <summary>Dosyanin dis kabugu. Sifreliyse icerik <see cref="Payload"/> icinde.</summary>
    private sealed class Envelope
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = FormatVersion;
        public bool Encrypted { get; set; }
        public string? Salt { get; set; }
        public int? Iterations { get; set; }
        public string? Payload { get; set; }
        public Bundle? Content { get; set; }
    }

    /// <summary>Sifrelenen (veya sifresiz yazilan) asil icerik.</summary>
    private sealed class Bundle
    {
        public List<RdpConnection> Connections { get; set; } = [];

        /// <summary>
        /// Baglanti Id -> duz metin parola.
        /// Ayri tutuluyor cunku RdpConnection.Password serialize edilmiyor
        /// (kazara diske yazilmasini onlemek icin JsonIgnore).
        /// </summary>
        public Dictionary<string, string> Secrets { get; set; } = [];
    }

    /// <summary>
    /// Disa aktarir. <paramref name="passphrase"/> bos ise parolalar yazilmaz.
    /// <paramref name="resolvePassword"/> her baglantinin duz metin parolasini
    /// cozmek icin cagrilir (repository'nin DPAPI cozucusu).
    /// </summary>
    public static void Export(
        string filePath,
        IEnumerable<RdpConnection> connections,
        string? passphrase,
        Func<RdpConnection, string?> resolvePassword)
    {
        var includeSecrets = !string.IsNullOrEmpty(passphrase);
        var bundle = new Bundle();

        foreach (var connection in connections)
        {
            var copy = connection.Clone();

            if (includeSecrets && resolvePassword(connection) is { Length: > 0 } plain)
                bundle.Secrets[copy.Id] = plain;

            // Makineye bagli blob tasinamaz; disari hic cikmiyor.
            copy.ProtectedPassword = null;
            copy.Password = null;
            bundle.Connections.Add(copy);
        }

        var envelope = new Envelope();

        if (includeSecrets)
        {
            var sealedContent = PassphraseCipher.Encrypt(
                JsonSerializer.Serialize(bundle, ConnectionRepository.JsonOptions), passphrase!);

            envelope.Encrypted = true;
            envelope.Salt = sealedContent.Salt;
            envelope.Iterations = PassphraseCipher.Iterations;
            envelope.Payload = sealedContent.Payload;
        }
        else
        {
            envelope.Content = bundle;
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(envelope, FileOptions));
        Logger.Information("{Count} baglanti disa aktarildi (parolali={Encrypted}): {Path}",
            bundle.Connections.Count, includeSecrets, filePath);
    }

    /// <summary>Dosya sifreli mi? Ice aktarmadan once parola sorulmali mi.</summary>
    public static bool IsEncrypted(string filePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Encrypted", out var flag)
                && flag.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ice aktarir. Id catismasini onlemek icin her kayda yeni Id verilir —
    /// mevcut kayitlarin uzerine yazilmaz, yanlarina eklenir.
    ///
    /// Cozulen parolalar <see cref="RdpConnection.Password"/> alanina konur;
    /// repository kaydederken bu makinenin DPAPI'siyla yeniden korur.
    /// </summary>
    public static IReadOnlyList<RdpConnection> Import(string filePath, string? passphrase = null)
    {
        var json = File.ReadAllText(filePath);
        var bundle = ReadBundle(json, passphrase);

        foreach (var item in bundle.Connections)
        {
            bundle.Secrets.TryGetValue(item.Id, out var plain);

            item.Id = Guid.NewGuid().ToString("N");
            item.SortOrder = 0;              // listenin sonuna eklensin
            item.ProtectedPassword = null;
            item.Password = string.IsNullOrEmpty(plain) ? null : plain;
            item.Tags ??= [];
        }

        Logger.Information("{Count} baglanti ice aktarildi: {Path}", bundle.Connections.Count, filePath);
        return bundle.Connections;
    }

    private static Bundle ReadBundle(string json, string? passphrase)
    {
        using var document = JsonDocument.Parse(json);

        // Eski surum: dosyanin kokunde dogrudan baglanti dizisi vardi.
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacy = JsonSerializer.Deserialize<List<RdpConnection>>(json, ConnectionRepository.JsonOptions)
                         ?? throw new InvalidDataException(Loc.T("Dosya bir baglanti listesi icermiyor."));
            return new Bundle { Connections = legacy };
        }

        var envelope = JsonSerializer.Deserialize<Envelope>(json, ConnectionRepository.JsonOptions)
                       ?? throw new InvalidDataException(Loc.T("Dosya taninmadi."));

        if (!envelope.Encrypted)
            return envelope.Content ?? throw new InvalidDataException(Loc.T("Dosya bos."));

        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidDataException(Loc.T("Bu dosya sifreli, parola gerekiyor."));

        var plainJson = PassphraseCipher.Decrypt(
            new PassphraseCipher.Envelope(envelope.Salt!, envelope.Payload!), passphrase);

        return JsonSerializer.Deserialize<Bundle>(plainJson, ConnectionRepository.JsonOptions)
               ?? throw new InvalidDataException(Loc.T("Sifreli icerik cozuldu ama okunamadi."));
    }
}
