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

        var draft = _viewModel.Draft;
        (draft.IsVnc ? TabVnc : draft.IsSsh ? TabSsh : TabRdp).IsChecked = true;
        (draft.SshUseKey ? SshKeyRadio : SshPasswordRadio).IsChecked = true;
        UpdateProtocolFields();
    }

    // FieldsGrid satirlari: 8 Domain, 9 Parola, 10-13 Goruntu, 14-16 Yonlendirmeler (+ses),
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
                14 or 15 => !draft.IsSsh, // VNC'de yalniz pano secenegi gosterilir
                19 => !draft.IsSsh,  // otomatik yeniden baglanma VNC'de de var
                _ => rdp || !RdpOnlyRows.Contains(row),
            };
            child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        CredSspCheck.Visibility = WindowsKeysCheck.Visibility = rdp ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement option in ((System.Windows.Controls.Panel)ClipboardCheck.Parent).Children)
            if (option != ClipboardCheck) option.Visibility = rdp ? Visibility.Visible : Visibility.Collapsed;

        var sshKey = draft.IsSsh && draft.SshUseKey;
        SshAuthPanel.Visibility = draft.IsSsh ? Visibility.Visible : Visibility.Collapsed;
        SshKeyPanel.Visibility = sshKey ? Visibility.Visible : Visibility.Collapsed;
        PasswordLabel.Text = draft.IsSsh ? Loc.T("Giris") : Loc.T("Parola");

        PasswordHint.Text = sshKey
            ? Loc.T("Anahtar sifreliyse parolasini yazin; degilse bos birakin.")
            : _viewModel.HadPassword
                ? Loc.T("Kayitli parola var. Degistirmek icin yenisini yazin, dokunmazsaniz korunur.")
                : draft.IsVnc
                    ? Loc.T("VNC sunucusu parola istiyorsa burada kaydedin; baglanirken sorulmaz.")
                    : draft.IsSsh
                        ? Loc.T("SSH girisi icin parolayi kaydedin.")
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

        if (draft.IsSsh && string.IsNullOrWhiteSpace(draft.UserName))
        {
            MessageBox.Show(this, Loc.T("SSH icin kullanici adi gerekli."), Loc.T("Eksik bilgi"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (draft.IsSsh && draft.SshUseKey && string.IsNullOrWhiteSpace(draft.SshKeyPath))
        {
            MessageBox.Show(this, Loc.T("SSH anahtar dosyasini secin."), Loc.T("Eksik bilgi"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = _viewModel.Commit(PasswordInput.Password, ClearPasswordBox.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Protokol sekmesi: alanlari guncelle, portu o protokolun varsayilanina cek (elle degistirilmediyse).</summary>
    private void ProtocolTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { Tag: string protocol }) return;
        var draft = _viewModel.Draft;
        draft.Protocol = protocol;
        if (!IsLoaded) return;
        UpdateProtocolFields();
        var defaults = new[] { "RDP", "VNC", "SSH" }.Select(RdpConnection.DefaultPort);
        // Kullanici portu elle degistirdiyse dokunma; yalniz baska protokolun varsayilanindaysa cevir.
        if (!defaults.Contains(draft.Port) || draft.Port == RdpConnection.DefaultPort(draft.Protocol)) return;
        draft.Port = RdpConnection.DefaultPort(draft.Protocol);
        PortInput.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }

    private void SshAuth_Checked(object sender, RoutedEventArgs e)
    {
        _viewModel.Draft.SshUseKey = SshKeyRadio.IsChecked == true;
        if (IsLoaded) UpdateProtocolFields();
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var sshDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("SSH ozel anahtari secin"),
            Filter = Loc.T("Tum dosyalar") + " (*.*)|*.*",
            InitialDirectory = System.IO.Directory.Exists(sshDir) ? sshDir : null,
        };
        if (dialog.ShowDialog(this) != true) return;
        _viewModel.Draft.SshKeyPath = dialog.FileName;
        SshKeyInput.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
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
