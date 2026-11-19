using System.IO;
using RdpManager.Data;
using RdpManager.Models;
using RdpManager.Security;
using Xunit;

namespace RdpManager.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RdpManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void History_start_end_and_clear()
    {
        var database = new Database(_directory);
        database.Initialize();
        var repository = new ConnectionRepository(database, new CredentialProtector());
        var connection = new RdpConnection { Name = "Muhasebe", Address = "10.0.0.5" };

        var id = repository.StartHistory(connection);
        Assert.NotNull(id);
        Assert.Null(Assert.Single(repository.GetHistory()).EndedAt);

        repository.EndHistory(id.Value);
        var entry = Assert.Single(repository.GetHistory());
        Assert.NotNull(entry.EndedAt);
        Assert.Equal("Muhasebe", entry.Name);

        repository.ClearHistory();
        Assert.Empty(repository.GetHistory());
    }

    [Theory]
    [InlineData(42, "42 sn")]
    [InlineData(600, "10 dk")]
    [InlineData(5400, "1 sa 30 dk")]
    public void Duration_is_formatted(long seconds, string expected)
        => Assert.Equal(expected, HistoryEntry.FormatDuration(seconds));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
