using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using RdpManager.Models;
using RdpManager.Security;
using Serilog;

namespace RdpManager.Data;

public sealed class ConnectionRepository
{
    private static readonly ILogger Logger = Log.ForContext<ConnectionRepository>();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Database _database;
    private readonly CredentialProtector _protector;

    public ConnectionRepository(Database database, CredentialProtector protector)
    {
        _database = database;
        _protector = protector;
    }

    /// <summary>Elle belirlenmis siraya gore dondurur.</summary>
    public IReadOnlyList<RdpConnection> GetAll()
    {
        using var connection = _database.Open();
        var rows = connection.Query<string>(
            "SELECT Json FROM Connections ORDER BY SortOrder, Name COLLATE NOCASE");

        var result = new List<RdpConnection>();
        foreach (var json in rows)
        {
            var item = Deserialize(json);
            if (item is not null) result.Add(item);
        }
        return result;
    }

    /// <summary>Ekle veya guncelle. Duz metin parola burada DPAPI ile korunur.</summary>
    public void Save(RdpConnection item)
    {
        // Editorden duz metin parola geldiyse koru; gelmediyse mevcut blob'a dokunma.
        if (item.Password is not null)
            item.ProtectedPassword = _protector.Protect(item.Password);

        item.SchemaVersion = RdpConnection.CurrentSchemaVersion;

        // Yeni kayit listenin sonuna gitsin.
        if (item.SortOrder == 0) item.SortOrder = NextSortOrder();

        using var connection = _database.Open();
        connection.Execute("""
            INSERT INTO Connections (Id, Name, Address, Tags, SortOrder, LastConnectedAt, SchemaVersion, UpdatedAt, Json)
            VALUES (@Id, @Name, @Address, @Tags, @SortOrder, @LastConnectedAt, @SchemaVersion, @UpdatedAt, @Json)
            ON CONFLICT(Id) DO UPDATE SET
                Name            = excluded.Name,
                Address         = excluded.Address,
                Tags            = excluded.Tags,
                SortOrder       = excluded.SortOrder,
                LastConnectedAt = excluded.LastConnectedAt,
                SchemaVersion   = excluded.SchemaVersion,
                UpdatedAt       = excluded.UpdatedAt,
                Json            = excluded.Json;
            """,
            new
            {
                item.Id,
                Name = item.DisplayName,
                item.Address,
                Tags = string.Join(",", item.Tags),
                item.SortOrder,
                item.LastConnectedAt,
                item.SchemaVersion,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Json = JsonSerializer.Serialize(item, JsonOptions),
            });

        Logger.Information("Baglanti kaydedildi: {Name}", item.DisplayName);
    }

    public void Delete(string id)
    {
        using var connection = _database.Open();
        connection.Execute("DELETE FROM Connections WHERE Id = @id", new { id });
        Logger.Information("Baglanti silindi: {Id}", id);
    }

    /// <summary>Elle siralama sonrasi tum sirayi tek islemde yazar.</summary>
    public void SaveOrder(IEnumerable<RdpConnection> ordered)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var index = 1;
        foreach (var item in ordered)
        {
            item.SortOrder = index++;
            connection.Execute(
                "UPDATE Connections SET SortOrder = @SortOrder WHERE Id = @Id",
                new { item.SortOrder, item.Id }, transaction);
        }

        transaction.Commit();
        Logger.Debug("Siralama kaydedildi ({Count} kayit)", index - 1);
    }

    /// <summary>Basarili baglanti sonrasi "son kullanim" damgasi.</summary>
    // ------------------------------------------------------------ gecmis

    private const int HistoryLimit = 5000;

    /// <summary>Yeni gecmis kaydi acar; kayit Id'sini dondurur (hata olursa null).</summary>
    public long? StartHistory(RdpConnection c)
    {
        try
        {
            using var connection = _database.Open();
            var id = connection.ExecuteScalar<long>("""
                INSERT INTO History (ConnectionId, Name, Address, Protocol, StartedAt)
                VALUES (@Id, @Name, @Address, @Protocol, @now);
                SELECT last_insert_rowid();
                """,
                new { c.Id, Name = c.DisplayName, c.Address, c.Protocol, now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

            // Tablo sinirsiz buyumesin.
            connection.Execute("DELETE FROM History WHERE Id <= @cut", new { cut = id - HistoryLimit });
            return id;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Gecmis kaydi yazilamadi");
            return null;
        }
    }

    public void EndHistory(long id)
    {
        try
        {
            using var connection = _database.Open();
            connection.Execute("UPDATE History SET EndedAt = @now WHERE Id = @id AND EndedAt IS NULL",
                new { id, now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Gecmis kaydi kapatilamadi");
        }
    }

    public IReadOnlyList<HistoryEntry> GetHistory(int limit = 500)
    {
        using var connection = _database.Open();
        return connection.Query<HistoryEntry>(
            "SELECT * FROM History ORDER BY StartedAt DESC, Id DESC LIMIT @limit", new { limit }).ToList();
    }

    public void ClearHistory()
    {
        using var connection = _database.Open();
        connection.Execute("DELETE FROM History");
    }

    public void MarkConnected(string id)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            using var connection = _database.Open();
            connection.Execute(
                "UPDATE Connections SET LastConnectedAt = @now WHERE Id = @id", new { now, id });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Son kullanim damgasi yazilamadi");
        }
    }

    /// <summary>
    /// Baglanmadan hemen once cagrilir: korunmus parolayi cozup
    /// <see cref="RdpConnection.Password"/> alanina yazar.
    /// Parolalar liste yuklenirken TOPLUCA cozulmez — sadece ihtiyac aninda.
    /// </summary>
    public void ResolvePassword(RdpConnection item)
        => item.Password = _protector.Unprotect(item.ProtectedPassword);

    // ------------------------------------------------------- kucuk tercihler

    public string? GetSetting(string key)
    {
        try
        {
            using var connection = _database.Open();
            return connection.QuerySingleOrDefault<string>(
                "SELECT Value FROM Settings WHERE Key = @key", new { key });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ayar okunamadi: {Key}", key);
            return null;
        }
    }

    public void SetSetting(string key, string value)
    {
        try
        {
            using var connection = _database.Open();
            connection.Execute("""
                INSERT INTO Settings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """, new { key, value });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ayar kaydedilemedi: {Key}", key);
        }
    }

    private int NextSortOrder()
    {
        using var connection = _database.Open();
        return connection.QuerySingle<int?>("SELECT MAX(SortOrder) FROM Connections") is { } max
            ? max + 1
            : 1;
    }

    private static RdpConnection? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RdpConnection>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Bozuk baglanti kaydi atlandi");
            return null;
        }
    }
}
