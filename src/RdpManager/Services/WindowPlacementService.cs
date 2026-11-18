using System.Text.Json;
using System.Windows;
using Dapper;
using RdpManager.Data;
using Serilog;

using Window = System.Windows.Window;

namespace RdpManager.Services;

/// <summary>
/// Pencere boyut/konumunu Settings tablosunda saklar ve geri yukler.
/// </summary>
public sealed class WindowPlacementService
{
    private static readonly ILogger Logger = Log.ForContext<WindowPlacementService>();

    private readonly Database _database;

    public WindowPlacementService(Database database) => _database = database;

    private sealed record Placement(double Left, double Top, double Width, double Height, bool Maximized);

    /// <summary>
    /// Kayitli yerlesim varsa uygular.
    /// Kayit yoksa <paramref name="maximizeByDefault"/> true ise pencere buyutulmus acilir.
    /// </summary>
    public void Restore(Window window, string key, bool maximizeByDefault = false)
    {
        var saved = Load(key);

        if (saved is null)
        {
            if (maximizeByDefault) window.WindowState = WindowState.Maximized;
            return;
        }

        // Kayitli monitor artik yoksa pencere ekran disinda kalabilir.
        if (IsOnScreen(saved))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = saved.Left;
            window.Top = saved.Top;
            window.Width = saved.Width;
            window.Height = saved.Height;
        }
        else
        {
            Logger.Debug("Kayitli konum ekran disinda, varsayilana donuluyor: {Key}", key);
        }

        if (saved.Maximized) window.WindowState = WindowState.Maximized;
    }

    public void Save(Window window, string key)
    {
        // Buyutulmusken Left/Top/Width/Height ekran degerleridir; onceki
        // normal boyutu RestoreBounds veriyor.
        var maximized = window.WindowState == WindowState.Maximized;
        var bounds = maximized ? window.RestoreBounds : new Rect(window.Left, window.Top, window.Width, window.Height);

        if (double.IsNaN(bounds.Width) || bounds.Width <= 0) return;

        Store(key, new Placement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized));
    }

    private static bool IsOnScreen(Placement placement)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        // Baslik cubugunun bir kismi gorunuyorsa yeterli.
        var titleBar = new Rect(placement.Left, placement.Top, placement.Width, 40);
        return virtualScreen.IntersectsWith(titleBar);
    }

    private Placement? Load(string key)
    {
        try
        {
            using var connection = _database.Open();
            var raw = connection.QuerySingleOrDefault<string>(
                "SELECT Value FROM Settings WHERE Key = @key", new { key = SettingKey(key) });

            return raw is null ? null : JsonSerializer.Deserialize<Placement>(raw);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Pencere yerlesimi okunamadi: {Key}", key);
            return null;
        }
    }

    private void Store(string key, Placement placement)
    {
        try
        {
            using var connection = _database.Open();
            connection.Execute("""
                INSERT INTO Settings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """,
                new { key = SettingKey(key), value = JsonSerializer.Serialize(placement) });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Pencere yerlesimi kaydedilemedi: {Key}", key);
        }
    }

    private static string SettingKey(string key) => $"window.placement.{key}";
}
