using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;

namespace RdpManager.Data;

/// <summary>
/// SQLite baglanti fabrikasi ve sema kurulumu.
///
/// Sema tasarimi: RDP'nin 40+ ayari tek bir JSON sutununda. Yeni ayar eklemek
/// DDL migration gerektirmiyor. Sadece listeleme/arama/siralama icin gereken
/// alanlar ayri sutunda — boylece liste cizerken yuzlerce JSON deserialize edilmiyor.
/// </summary>
public sealed class Database
{
    private static readonly ILogger Logger = Log.ForContext<Database>();

    private readonly string _connectionString;

    public Database(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, "data.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public string DatabasePath { get; }

    public IDbConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        using var connection = Open();

        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Connections (
                Id            TEXT    PRIMARY KEY NOT NULL,
                Name          TEXT    NOT NULL,
                Address       TEXT    NOT NULL,
                SchemaVersion INTEGER NOT NULL,
                UpdatedAt     INTEGER NOT NULL,
                Json          TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY NOT NULL,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS History (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                ConnectionId TEXT    NOT NULL,
                Name         TEXT    NOT NULL,
                Address      TEXT    NOT NULL,
                Protocol     TEXT    NOT NULL,
                StartedAt    INTEGER NOT NULL,
                EndedAt      INTEGER
            );
            CREATE INDEX IF NOT EXISTS IX_History_StartedAt ON History (StartedAt);
            """);

        // Eski surumlerden gelen veritabanlari icin eksik sutunlari ekle.
        EnsureColumn(connection, "Tags", "TEXT");
        EnsureColumn(connection, "SortOrder", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "LastConnectedAt", "INTEGER");

        connection.Execute("""
            CREATE INDEX IF NOT EXISTS IX_Connections_Name  ON Connections (Name);
            CREATE INDEX IF NOT EXISTS IX_Connections_Order ON Connections (SortOrder);
            """);

        Logger.Information("Veritabani hazir: {Path}", DatabasePath);
    }

    /// <summary>
    /// SQLite'ta "ADD COLUMN IF NOT EXISTS" yok; once sutun listesine bakiyoruz.
    /// </summary>
    private static void EnsureColumn(IDbConnection connection, string column, string definition)
    {
        var existing = connection.Query("PRAGMA table_info(Connections);")
            .Select(row => (string)((IDictionary<string, object>)row)["name"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (existing.Contains(column)) return;

        connection.Execute($"ALTER TABLE Connections ADD COLUMN {column} {definition};");
        Logger.Information("Sema guncellendi: Connections.{Column} eklendi", column);
    }
}
