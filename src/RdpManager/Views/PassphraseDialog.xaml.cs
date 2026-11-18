using System.Windows;
using RdpManager.Localization;
using RdpManager.Services;

namespace RdpManager.Views;

/// <summary>
/// Disa/ice aktarma parolasini sorar.
/// Disa aktarmada parola BOS birakilabilir — o zaman sifreler dosyaya yazilmaz.
/// </summary>
public partial class PassphraseDialog : ChromeWindow
{
    private readonly bool _isExport;

    private PassphraseDialog(bool isExport)
    {
        _isExport = isExport;
        InitializeComponent();
        WindowTheming.Track(this);

        if (isExport)
        {
            Title = Loc.T("Disa aktarma parolasi");
            ExplanationText.Text =
                Loc.T("Kayitli parolalarin da disa aktarilmasi icin bir parola belirleyin. "
                + "Dosya bu parolayla sifrelenir (AES-256), ice aktarirken ayni parola sorulur.");
            HintText.Text =
                Loc.T("Bos birakirsaniz baglantilar parolasiz disa aktarilir.\n"
                + "Bu parola hicbir yerde saklanmaz - kaybederseniz dosyadaki parolalar kurtarilamaz.");
        }
        else
        {
            Title = Loc.T("Ice aktarma parolasi");
            ExplanationText.Text = Loc.T("Bu dosya sifrelenmis parolalar iceriyor. Disa aktarirken belirlediginiz parolayi girin.");
            ConfirmSection.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Collapsed;
        }

        UpdateOkState();
    }

    public string Passphrase { get; private set; } = string.Empty;

    /// <summary>Disa aktarma icin sorar. Iptal edilirse null doner.</summary>
    public static string? AskForExport(Window owner)
    {
        var dialog = new PassphraseDialog(isExport: true) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    /// <summary>Ice aktarma icin sorar. Iptal edilirse null doner.</summary>
    public static string? AskForImport(Window owner)
    {
        var dialog = new PassphraseDialog(isExport: false) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => UpdateOkState();

    private void UpdateOkState()
    {
        if (!_isExport)
        {
            // Ice aktarmada parola zorunlu.
            OkButton.IsEnabled = ConfirmInput is not null && PassphraseInput.Password.Length > 0;
            return;
        }

        // Disa aktarmada bos parola gecerli (sifresiz aktarim) ama
        // doluysa iki alan birbirini tutmali.
        var passphrase = PassphraseInput.Password;
        var matches = passphrase == ConfirmInput.Password;

        OkButton.IsEnabled = matches;
        OkButton.Content = passphrase.Length == 0 ? Loc.T("Parolasiz aktar") : Loc.T("Disa aktar");

        HintText.Foreground = matches
            ? (System.Windows.Media.Brush)FindResource("Brush.TextMuted")
            : (System.Windows.Media.Brush)FindResource("Brush.Danger");

        if (!matches) HintText.Text = Loc.T("Parolalar birbirini tutmuyor.");
        else if (passphrase.Length == 0)
            HintText.Text = Loc.T("Bos birakirsaniz baglantilar parolasiz disa aktarilir.\n"
                          + "Bu parola hicbir yerde saklanmaz - kaybederseniz dosyadaki parolalar kurtarilamaz.");
        else
            HintText.Text = Loc.T("Bu parola hicbir yerde saklanmaz - kaybederseniz dosyadaki parolalar kurtarilamaz.");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Passphrase = PassphraseInput.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
