using System.Reflection;
using System.Windows;
using RdpManager.Localization;
using RdpManager.Models;
using Serilog;

using Application = System.Windows.Application;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace RdpManager.Services;

/// <summary>
/// Windows bildirim alani (tray) ikonu.
///
/// Ana pencere kapatildiginda uygulama tray'e cekilir (1Remote gibi);
/// tam cikis yalniz tray menusunden veya ikona cift tiklayarak degil,
/// menudeki "Cikis" ile yapilir.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<TrayIconService>();

    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;

    private Func<IReadOnlyList<RdpConnection>>? _connectionsSource;
    private Action<RdpConnection>? _connect;

    /// <summary>Tray'den "Goster" secildiginde veya ikona cift tiklandiginda.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>Tray'den "Cikis" secildiginde.</summary>
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = AppEdition.Name,
            Icon = LoadIcon(),
            Visible = true,
        };

        _menu = new WinForms.ContextMenuStrip();
        _menu.Opening += (_, _) => RebuildMenu();
        _icon.ContextMenuStrip = _menu;

        RebuildMenu();

        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hizli baglan menusu icin baglanti kaynagi ve baglanma eylemini kaydeder.
    /// </summary>
    public void Initialize(Func<IReadOnlyList<RdpConnection>> connectionsSource, Action<RdpConnection> connect)
    {
        _connectionsSource = connectionsSource;
        _connect = connect;
    }

    /// <summary>Menuyu yeniden kurar: Goster / favoriler / son kullanilanlar / Cikis.</summary>
    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(Loc.T("Goster"), null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));

        var all = _connectionsSource?.Invoke();
        if (all is { Count: > 0 } && _connect is not null)
        {
            var favorites = all.Where(c => c.IsFavorite)
                               .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                               .ToList();

            var recent = all.Where(c => !c.IsFavorite && c.LastConnectedAt is not null)
                            .OrderByDescending(c => c.LastConnectedAt)
                            .Take(8)
                            .ToList();

            if (favorites.Count > 0)
            {
                _menu.Items.Add(new WinForms.ToolStripSeparator());
                _menu.Items.Add(Header(Loc.T("Favoriler")));
                foreach (var c in favorites) _menu.Items.Add(ConnectItem(c));
            }

            if (recent.Count > 0)
            {
                _menu.Items.Add(new WinForms.ToolStripSeparator());
                _menu.Items.Add(Header(Loc.T("Son kullanilanlar")));
                foreach (var c in recent) _menu.Items.Add(ConnectItem(c));
            }
        }

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(Loc.T("Cikis"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    private static WinForms.ToolStripMenuItem Header(string text)
    {
        var item = new WinForms.ToolStripMenuItem(text) { Enabled = false };
        return item;
    }

    private WinForms.ToolStripMenuItem ConnectItem(RdpConnection c)
    {
        var item = new WinForms.ToolStripMenuItem($"{c.Icon}  {c.DisplayName}");
        item.Click += (_, _) => Application.Current?.Dispatcher.Invoke(() => _connect?.Invoke(c));
        return item;
    }

    /// <summary>
    /// Windows bildirimi (toast) gosterir. Win10/11'de balon ipuclari Eylem
    /// Merkezi'ne toast olarak yonlendirilir.
    /// </summary>
    public void Notify(string title, string message)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = WinForms.ToolTipIcon.Warning;
            _icon.ShowBalloonTip(5000);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Bildirim gosterilemedi");
        }
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico");
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                using (stream)
                    return new Drawing.Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Tray ikonu yuklenemedi, exe ikonu deneniyor");
        }

        try
        {
            var exe = Assembly.GetExecutingAssembly().Location;
            var extracted = Drawing.Icon.ExtractAssociatedIcon(exe);
            if (extracted is not null) return extracted;
        }
        catch { /* son care */ }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
