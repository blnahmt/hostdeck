using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RdpManager.Models;
using RdpManager.Rdp;
using RdpManager.Services;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RdpManager.Views;

/// <summary>Komut paletinde bir satir: aramaya '>' yazinca listelenir.</summary>
public sealed record PaletteCommand(string Title, string Hint, Action Run)
{
    // Ekran okuyucu ListBoxItem adini ToString'den aliyor; kayit dokumu degil baslik okunsun.
    public override string ToString() => Title;
}

/// <summary>
/// Alt+M ile acilan hizli baglanti penceresi.
/// Yaz, Enter'a bas, baglan — sunucu listesi penceresini hic acmadan.
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly SessionManager _sessions;
    private readonly ServerStatusService _serverStatus;

    /// <summary>Ana pencere tarafindan baslangicta doldurulur.</summary>
    public IReadOnlyList<PaletteCommand> Commands { get; set; } = [];

    public LauncherWindow(SessionManager sessions, ServerStatusService serverStatus)
    {
        _sessions = sessions;
        _serverStatus = serverStatus;
        InitializeComponent();

        // Odak disina cikinca kapansin (komut paleti davranisi).
        Deactivated += (_, _) => Hide();

        // Yeni sunucu durumu geldiginde acik listeyi guncelle.
        _serverStatus.StatusChanged += (_, _) => Dispatcher.Invoke(ReapplyStatus);
    }

    private void ReapplyStatus()
    {
        if (IsVisible && ResultList.ItemsSource is IEnumerable<RdpConnection> items)
            _serverStatus.ApplyTo(items);
    }

    /// <summary>Kisayola basildiginda cagrilir. <paramref name="initial"/> ">" ise komut modunda acilir.</summary>
    public void ShowLauncher(string initial = "")
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        _serverStatus.RefreshNow();
        SearchBox.Text = initial;
        SearchBox.CaretIndex = initial.Length;
        Refresh();

        PositionOnCursorScreen();
        Show();
        Activate();

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>
    /// Imlecin bulundugu monitorde, ust ucte acilir — komut paletlerinin
    /// alistigimiz yeri burasi ve pencere kenarlarina takilmaz.
    /// </summary>
    private void PositionOnCursorScreen()
    {
        var (cursorX, cursorY) = NativeMethods.GetCursorPosition();
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursorX, cursorY));
        var bounds = screen.WorkingArea;

        var dpi = NativeMethods.GetMonitorDpi(
            bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var scale = dpi / 96.0;

        // Olculmemis olabilir; genislik sabit oldugu icin onu kullaniyoruz.
        Left = (bounds.Left + bounds.Width / 2.0) / scale - Width / 2;
        Top = bounds.Top / scale + 140;
    }

    private void Refresh()
    {
        var search = SearchBox.Text.Trim();

        if (search.StartsWith('>'))
        {
            var query = search[1..].Trim();
            var commands = Commands
                .Where(c => query.Length == 0 || c.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            ResultList.ItemsSource = commands;
            if (commands.Count > 0) ResultList.SelectedIndex = 0;
            return;
        }

        var items = _sessions.GetConnections()
            .Where(c => string.IsNullOrEmpty(search) || Matches(c, search))
            .OrderByDescending(c => c.LastConnectedAt ?? 0)
            .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(50)
            .ToList();

        _serverStatus.ApplyTo(items);
        ResultList.ItemsSource = items;
        if (items.Count > 0) ResultList.SelectedIndex = 0;
    }

    private static bool Matches(RdpConnection connection, string search)
        => connection.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
           || connection.Address.Contains(search, StringComparison.OrdinalIgnoreCase)
           || connection.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase));

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Connect();
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Connect();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Connect();

    /// <summary>Ok tuslari arama kutusundan cikmadan listede gezdirir.</summary>
    private void MoveSelection(int delta)
    {
        if (ResultList.Items.Count == 0) return;

        var index = Math.Clamp(ResultList.SelectedIndex + delta, 0, ResultList.Items.Count - 1);
        ResultList.SelectedIndex = index;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void Connect()
    {
        if (ResultList.SelectedItem is PaletteCommand command)
        {
            Hide();
            command.Run();
            return;
        }

        if (ResultList.SelectedItem is not RdpConnection connection) return;

        Hide();
        _sessions.Open(connection);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Uygulama kapanmadikca pencere yok edilmesin; tekrar tekrar kullaniliyor.
        if (!IsApplicationShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    internal bool IsApplicationShuttingDown { get; set; }
}
