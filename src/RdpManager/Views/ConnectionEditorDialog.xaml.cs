using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RdpManager.Models;
using RdpManager.Localization;
using RdpManager.Services;
using RdpManager.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager.Views;

public partial class ConnectionEditorDialog : ChromeWindow
{
    private readonly ConnectionEditorViewModel _viewModel;

    public ConnectionEditorDialog(RdpConnection connection, bool isNew, IEnumerable<string>? knownTags = null)
    {
        InitializeComponent();
        WindowTheming.Track(this);

        _viewModel = new ConnectionEditorViewModel(connection, knownTags);
        DataContext = _viewModel;

        IconItems.ItemsSource = EmojiCatalog.All;
        IconGlyph.Text = string.IsNullOrEmpty(_viewModel.Draft.Icon) ? "🖥️" : _viewModel.Draft.Icon;

        Title = isNew ? Loc.T("Yeni baglanti") : Loc.F("Duzenle - {0}", _viewModel.Draft.DisplayName);

        ClearPasswordBox.Visibility = _viewModel.HadPassword ? Visibility.Visible : Visibility.Collapsed;
        UpdateProtocolFields();
    }

    // FieldsGrid satirlari: 8 Domain, 9 Parola, 10-16 Goruntu + Yonlendirmeler,
    // 17-18 Guvenlik, 19 secenekler, 20-21 RD Gateway. Satir eklenirse burasi da guncellenmeli.
    private static readonly HashSet<int> RdpOnlyRows = [8, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 21];

    /// <summary>Yalniz secili protokolde anlami olan alanlari gosterir (VNC/SSH'ta RDP ayarlari gizlenir).</summary>
    private void UpdateProtocolFields()
    {
        var draft = _viewModel.Draft;
        var rdp = !draft.IsVnc && !draft.IsSsh;

        foreach (UIElement child in FieldsGrid.Children)
        {
            var row = Grid.GetRow(child);
            var visible = row switch
            {
                9 => !draft.IsSsh,   // ssh.exe parolayi disaridan almiyor
                19 => !draft.IsSsh,  // otomatik yeniden baglanma VNC'de de var
                _ => rdp || !RdpOnlyRows.Contains(row),
            };
            child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        CredSspCheck.Visibility = WindowsKeysCheck.Visibility = rdp ? Visibility.Visible : Visibility.Collapsed;

        PasswordHint.Text = _viewModel.HadPassword
            ? Loc.T("Kayitli parola var. Degistirmek icin yenisini yazin, dokunmazsaniz korunur.")
            : draft.IsVnc
                ? Loc.T("VNC sunucusu parola istiyorsa burada kaydedin; baglanirken sorulmaz.")
                : Loc.T("Parola kaydedilmezse baglanirken Windows sorar.");
    }

    public RdpConnection? Result { get; private set; }

    private void TagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel.AddTagCommand.Execute(null);
        e.Handled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var draft = _viewModel.Draft;

        if (string.IsNullOrWhiteSpace(draft.Address))
        {
            MessageBox.Show(this, Loc.T("Sunucu adresi bos olamaz."), Loc.T("Eksik bilgi"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (draft.Port is < 1 or > 65535)
        {
            MessageBox.Show(this, Loc.T("Port 1-65535 araliginda olmali."), Loc.T("Gecersiz port"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = _viewModel.Commit(PasswordInput.Password, ClearPasswordBox.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Protokol degisince portu o protokolun varsayilanina cek (elle degistirilmediyse).</summary>
    private void ProtocolCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateProtocolFields();
        var draft = _viewModel.Draft;
        var defaults = new[] { "RDP", "VNC", "SSH" }.Select(RdpConnection.DefaultPort);
        // Kullanici portu elle degistirdiyse dokunma; yalniz baska protokolun varsayilanindaysa cevir.
        if (!defaults.Contains(draft.Port) || draft.Port == RdpConnection.DefaultPort(draft.Protocol)) return;
        draft.Port = RdpConnection.DefaultPort(draft.Protocol);
        PortInput.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }

    private void IconButton_Click(object sender, RoutedEventArgs e)
        => IconPopup.IsOpen = !IconPopup.IsOpen;

    private void IconChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string emoji })
        {
            _viewModel.Draft.Icon = emoji;
            IconGlyph.Text = emoji;
        }
        IconPopup.IsOpen = false;
    }
}
