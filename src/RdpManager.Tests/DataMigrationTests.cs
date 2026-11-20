using System.IO;
using Xunit;

namespace RdpManager.Tests;

public class DataMigrationTests
{
    [Fact]
    public void Moves_legacy_folder_only_when_target_is_missing()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var target = Path.Combine(root, "Hostdeck");
            Directory.CreateDirectory(Path.Combine(root, "HostlineRD"));
            File.WriteAllText(Path.Combine(root, "HostlineRD", "connections.db"), "x");

            Assert.Equal(Path.Combine(root, "HostlineRD"), App.MoveLegacyDataDirectory(root, target, ["Missing", "HostlineRD"]));
            Assert.True(File.Exists(Path.Combine(target, "connections.db")));
            Assert.False(Directory.Exists(Path.Combine(root, "HostlineRD")));

            // Hedef varsa eski klasora dokunulmaz.
            Directory.CreateDirectory(Path.Combine(root, "HostlineRD"));
            Assert.Null(App.MoveLegacyDataDirectory(root, target, ["HostlineRD"]));
            Assert.True(Directory.Exists(Path.Combine(root, "HostlineRD")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
