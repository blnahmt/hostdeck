using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using RdpManager.Localization;
using RdpManager.Services;
using RdpManager.ViewModels;

using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using Border = System.Windows.Controls.Border;

namespace RdpManager.Views;

public partial class ApiKeyDialog : ChromeWindow
{
    // Hazir ana renk secenekleri.
    private static readonly string[] AccentPalette =
    [
        "#D64545", // varsayilan kirmizi
        "#E4572E", // turuncu
        "#E0A100", // amber
        "#3F9D5B", // yesil
        "#2E9B94", // teal
        "#3B82C4", // mavi
        "#5B5BD6", // indigo
        "#8B5CF6", // mor
        "#C2569B", // pembe
        "#64748B", // gri-mavi
    ];

    private readonly UpdateService _update;
    private readonly ThemeService _theme;
    private readonly MainViewModel _main;
    private bool _ready;

    public ApiKeyDialog(string? currentKey, UpdateService update, ThemeService theme, MainViewModel main)
    {
        _update = update;
        _theme = theme;
        _main = main;
        InitializeComponent();
        WindowTheming.Track(this);
        ApiKeyInput.Text = currentKey ?? string.Empty;

        // Gorunum secimlerini mevcut duruma gore ayarla.
        ThemeCombo.SelectedIndex = _theme.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0,
        };
        LanguageCombo.SelectedIndex = Localization.Loc.IsEnglish ? 1 : 0;
        PaletteCombo.ItemsSource = ThemeService.Palettes;
        PaletteCombo.SelectedItem = _theme.Palette;
        ViewCombo.SelectedIndex = _main.IsGridView ? 1 : 0;
        DensityCombo.SelectedIndex = _main.IsCompact ? 1 : 0;
        ThumbnailCheck.IsChecked = _main.ShowThumbnails;
        ClearThumbsCheck.IsChecked = _main.ClearThumbnailsOnExit;
        DefaultUserInput.Text = _main.DefaultUserName;
        DefaultDomainInput.Text = _main.DefaultDomain;
        AutoCheckBox.IsChecked = _update.AutoCheck;
        ScaleCombo.SelectedIndex = _main.UiScale switch
        {
            <= 0.9 => 0,
            >= 1.3 => 3,
            >= 1.15 => 2,
            _ => 1,
        };

        BuildAccentSwatches();
        CustomAccentInput.Text = _theme.AccentColor ?? _theme.DefaultAccent;

        VersionText.Text = Loc.F("Mevcut surum: v{0}", UpdateService.CurrentVersion);

        // Hakkinda
        AboutAppName.Text = AppEdition.Name;
        AboutVersion.Text = Loc.F("Surum {0}", UpdateService.CurrentVersion);
        GitHubLine.Visibility = AppEdition.IsPublic ? Visibility.Visible : Visibility.Collapsed; // ic surum herkese acik depoya baglanmasin
        UpdateDeveloperLogo();
        _theme.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => _theme.ThemeChanged -= OnThemeChanged;
        RefreshUpdateStatus();

        if (!AppEdition.HasServerStatus) NavApi.Visibility = Visibility.Collapsed;

        _ready = true;
        NavList.SelectedIndex = 0;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateDeveloperLogo();

    /// <summary>Lacivert logo acik temada, beyaz logo koyu temada okunur.</summary>
    private void UpdateDeveloperLogo()
        => DeveloperLogo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(
            $"pack://application:,,,/Resources/Developer/ahmet-bilen-{(_theme.IsDarkActive ? "dark" : "light")}.png"));

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var pages = new[] { PageAppearance, PageAccent, PageDefaults, PageApi, PageUpdate, PageLicenses };
        var index = NavList.SelectedIndex;
        for (var i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }

    public string ResultKey { get; private set; } = string.Empty;

    public static string? Show(Window owner, string? currentKey, UpdateService update, ThemeService theme, MainViewModel main)
    {
        var dialog = new ApiKeyDialog(currentKey, update, theme, main) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ResultKey : null;
    }

    // ------------------------------------------------------------- gorunum

    private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _theme.Apply(ThemeCombo.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System,
        });
    }

    private void LanguageCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        if (language == Localization.Loc.Language) return;

        App.Services.GetRequiredService<Data.ConnectionRepository>().SetSetting(Localization.Loc.SettingKey, language);

        // Soru yeni dilde: kullanici zaten o dili sectigi icin onu okuyabilir.
        var english = language == "en";
        var answer = MessageBox.Show(this,
            english ? "The language will change after a restart. Restart now?"
                    : "Dil degisikligi yeniden baslatinca uygulanir. Simdi yeniden baslatilsin mi?",
            english ? "Language" : "Dil", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) App.RestartApplication();
    }

    private void PaletteCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || PaletteCombo.SelectedItem is not string palette) return;
        _theme.SetPalette(palette);
        if (_theme.AccentColor is null) CustomAccentInput.Text = _theme.DefaultAccent;
    }

    private void ViewCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _main.IsGridView = ViewCombo.SelectedIndex == 1;
    }

    private void DensityCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _main.IsCompact = DensityCombo.SelectedIndex == 1;
    }

    private void ThumbnailCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.ShowThumbnails = ThumbnailCheck.IsChecked == true;
    }

    private void ClearThumbs_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.ClearThumbnailsOnExit = ClearThumbsCheck.IsChecked == true;
    }

    private void DeleteThumbs_Click(object sender, RoutedEventArgs e)
    {
        ThumbnailStore.Clear();
        MessageBox.Show(this, Loc.T("Kucuk resimler silindi."), Loc.T("Kucuk resim"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenThumbsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(ThumbnailStore.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ThumbnailStore.Directory,
                UseShellExecute = true,
            });
        }
        catch { /* acilamadi */ }
    }

    private void AutoCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _update.AutoCheck = AutoCheckBox.IsChecked == true;
    }

    private void ScaleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (ScaleCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            double.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out var scale))
        {
            _main.UiScale = scale;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Varsayilan baglanti alanlarini kaydet.
        _main.DefaultUserName = DefaultUserInput.Text.Trim();
        _main.DefaultDomain = DefaultDomainInput.Text.Trim();

        ResultKey = ApiKeyInput.Text.Trim();
        DialogResult = true;
    }

    private void Link_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch { /* tarayici acilamadi */ }
        e.Handled = true;
    }

    // ------------------------------------------------------------- ana renk

    private void BuildAccentSwatches()
    {
        var active = _theme.AccentColor ?? _theme.DefaultAccent;

        foreach (var hex in AccentPalette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var isActive = string.Equals(hex, active, StringComparison.OrdinalIgnoreCase);

            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)FindResource("Brush.Text"),
                BorderThickness = new Thickness(isActive ? 3 : 0),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += Swatch_Click;
            AccentSwatches.Children.Add(swatch);
        }
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string hex })
        {
            _theme.SetAccent(hex);
            CustomAccentInput.Text = hex;
            RefreshSwatchSelection(hex);
        }
    }

    private void RefreshSwatchSelection(string activeHex)
    {
        foreach (var child in AccentSwatches.Children)
        {
            if (child is Border b && b.Tag is string hex)
                b.BorderThickness = new Thickness(
                    string.Equals(hex, activeHex, StringComparison.OrdinalIgnoreCase) ? 3 : 0);
        }
    }

    private void ApplyCustomAccent_Click(object sender, RoutedEventArgs e)
    {
        var hex = CustomAccentInput.Text.Trim();
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            MessageBox.Show(this, Loc.T("Gecerli bir renk kodu girin (orn. #3B82C4)."),
                Loc.T("Gecersiz renk"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _theme.SetAccent(hex);
        RefreshSwatchSelection(hex);
    }

    private void ResetAccent_Click(object sender, RoutedEventArgs e)
    {
        _theme.SetAccent(null);
        CustomAccentInput.Text = _theme.DefaultAccent;
        RefreshSwatchSelection(_theme.DefaultAccent);
    }

    private void RefreshUpdateStatus()
    {
        if (_update.IsUpdateAvailable)
        {
            UpdateStatusText.Text = Loc.F("Yeni surum mevcut: v{0}", _update.LatestVersion);
            UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Success");
            UpdateButton.Content = Loc.T("Guncelle");
        }
        else
        {
            UpdateStatusText.Text = Loc.T("Guncel sürüm kullaniyorsunuz.");
            UpdateButton.Content = Loc.T("Kontrol et");
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_update.IsUpdateAvailable)
        {
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = Loc.T("Indiriliyor...");
            try
            {
                await _update.DownloadAndInstallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("Guncelleme indirilemedi:\n{0}", ex.Message),
                    Loc.T("Guncelleme hatasi"), MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButton.IsEnabled = true;
                RefreshUpdateStatus();
            }
            return;
        }

        UpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Loc.T("Kontrol ediliyor...");
        UpdateButton.Content = Loc.T("Kontrol ediliyor...");

        await _update.CheckAsync();

        UpdateButton.IsEnabled = true;
        RefreshUpdateStatus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
