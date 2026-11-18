using System.Windows;
using System.Windows.Input;
using RdpManager.Data;
using RdpManager.Models;
using RdpManager.Services;
using RdpManager.Localization;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager.Views;

public partial class HistoryWindow : ChromeWindow
{
    private readonly ConnectionRepository _repository;
    private readonly SessionManager _sessions;

    public HistoryWindow(ConnectionRepository repository, SessionManager sessions)
    {
        _repository = repository;
        _sessions = sessions;
        InitializeComponent();
        WindowTheming.Track(this);
        Load();
    }

    private void Load()
    {
        var items = _repository.GetHistory();
        HistoryList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reconnect()
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry) return;

        var connection = _repository.GetAll().FirstOrDefault(c => c.Id == entry.ConnectionId);
        if (connection is null)
        {
            MessageBox.Show(this, Loc.F("'{0}' baglantisi listeden silinmis.", entry.Name), Loc.T("Baglanti bulunamadi"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _sessions.Open(connection);
        Load(); // SSH aninda yeni kayit ekliyor
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Reconnect();

    private void HistoryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Reconnect();
        e.Handled = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Tum baglanti gecmisi silinsin mi? Bu islem geri alinamaz."),
            Loc.T("Gecmisi temizle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        _repository.ClearHistory();
        Load();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
