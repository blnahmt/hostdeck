using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Shell;
using RdpManager.Localization;
using RdpManager.Models;
using Serilog;

using Application = System.Windows.Application;

namespace RdpManager.Services;

/// <summary>
/// Windows gorev cubugu Jump List'ini kurar: favori ve son kullanilan sunucular.
/// Ogeye tiklaninca uygulama "--connect {id}" ile calisir; calisan instance
/// bunu yakalayip o sunucuya baglanir (bkz. App.OnStartup).
/// </summary>
public static class JumpListService
{
    private static readonly ILogger Logger = Log.ForContext(typeof(JumpListService));

    public static void Build(IReadOnlyList<RdpConnection> connections)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? Assembly.GetExecutingAssembly().Location;

            var list = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };

            var favorites = connections.Where(c => c.IsFavorite)
                .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var recent = connections.Where(c => !c.IsFavorite && c.LastConnectedAt is not null)
                .OrderByDescending(c => c.LastConnectedAt)
                .Take(8)
                .ToList();

            foreach (var c in favorites) list.JumpItems.Add(MakeTask(c, exePath, Loc.T("Favoriler")));
            foreach (var c in recent) list.JumpItems.Add(MakeTask(c, exePath, Loc.T("Son kullanilanlar")));

            list.Apply();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Jump List kurulamadi");
        }
    }

    private static JumpTask MakeTask(RdpConnection c, string exePath, string category) => new()
    {
        Title = c.DisplayName,
        Description = string.IsNullOrEmpty(c.Address) ? c.DisplayName : c.Address,
        ApplicationPath = exePath,
        Arguments = $"--connect {c.Id}",
        IconResourcePath = exePath,
        CustomCategory = category,
    };
}
