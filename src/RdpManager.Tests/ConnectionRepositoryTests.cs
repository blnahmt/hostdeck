using System.IO;
using Dapper;
using RdpManager.Data;
using RdpManager.Models;
using RdpManager.Security;
using Xunit;

namespace RdpManager.Tests;

public sealed class ConnectionRepositoryTests : IDisposable
{
    private readonly string _directory;
    private readonly Database _database;
    private readonly ConnectionRepository _repository;

    public ConnectionRepositoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "RdpManagerTests", Guid.NewGuid().ToString("N"));
        _database = new Database(_directory);
        _database.Initialize();
        _repository = new ConnectionRepository(_database, new CredentialProtector());
    }

    private static RdpConnection Sample() => new()
    {
        Name = "Muhasebe Sunucu",
        Tags = ["Uretim", "Kritik"],
        Color = "#D64545",
        Address = "10.0.0.5",
        Port = 3390,
        UserName = "admin",
        Domain = "SIRKET",
        Password = "GizliParola1",
        RedirectDrives = true,
        AudioRedirectionMode = 2,
        ColorDepth = 24,
    };

    [Fact]
    public void Save_then_GetAll_round_trips_all_settings()
    {
        var original = Sample();

        _repository.Save(original);
        var loaded = Assert.Single(_repository.GetAll());

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("Muhasebe Sunucu", loaded.Name);
        Assert.Equal(["Uretim", "Kritik"], loaded.Tags);
        Assert.Equal("#D64545", loaded.Color);
        Assert.Equal("10.0.0.5", loaded.Address);
        Assert.Equal(3390, loaded.Port);
        Assert.Equal("admin", loaded.UserName);
        Assert.Equal("SIRKET", loaded.Domain);
        Assert.True(loaded.RedirectDrives);
        Assert.Equal(2, loaded.AudioRedirectionMode);
        Assert.Equal(24, loaded.ColorDepth);
    }

    [Fact]
    public void Plaintext_password_is_never_written_to_disk()
    {
        _repository.Save(Sample());

        using var connection = _database.Open();
        var json = connection.QuerySingle<string>("SELECT Json FROM Connections");

        Assert.DoesNotContain("GizliParola1", json, StringComparison.Ordinal);
        Assert.Contains("ProtectedPassword", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Password_is_not_loaded_until_ResolvePassword_is_called()
    {
        _repository.Save(Sample());

        var loaded = _repository.GetAll().Single();
        Assert.Null(loaded.Password);
        Assert.True(loaded.HasPassword);

        _repository.ResolvePassword(loaded);
        Assert.Equal("GizliParola1", loaded.Password);
    }

    [Fact]
    public void Saving_without_password_keeps_the_existing_one()
    {
        var item = Sample();
        _repository.Save(item);

        // Editorde parola alani bos birakildi.
        var edited = _repository.GetAll().Single();
        edited.Name = "Yeni ad";
        edited.Password = null;
        _repository.Save(edited);

        var reloaded = _repository.GetAll().Single();
        _repository.ResolvePassword(reloaded);

        Assert.Equal("Yeni ad", reloaded.Name);
        Assert.Equal("GizliParola1", reloaded.Password);
    }

    [Fact]
    public void Save_updates_instead_of_duplicating()
    {
        var item = Sample();
        _repository.Save(item);

        item.Address = "10.0.0.9";
        _repository.Save(item);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal("10.0.0.9", all[0].Address);
    }

    [Fact]
    public void Delete_removes_the_row()
    {
        var item = Sample();
        _repository.Save(item);

        _repository.Delete(item.Id);

        Assert.Empty(_repository.GetAll());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch { /* temp, onemli degil */ }
    }
}
