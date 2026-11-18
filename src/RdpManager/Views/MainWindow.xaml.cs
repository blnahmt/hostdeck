using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RdpManager.Data;
using RdpManager.Models;
using RdpManager.Services;
using RdpManager.ViewModels;
using RdpManager.Localization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace RdpManager.Views;

/// <summary>
/// Sunucu listesi penceresi. Oturumlar burada acilmaz - ayri bir
/// <see cref="SessionWindow"/> icinde sekme olarak acilir.
/// </summary>
public partial class MainWindow : ChromeWindow
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();

    private readonly MainViewModel _viewModel;
    private readonly SessionManager _sessions;
    private readonly ThemeService _theme;

    private Point _dragStart;
    private RdpConnection? _dragCandidate;

    private readonly WindowPlacementService _placement;
    private readonly HotkeyService _hotkeys;

    private readonly ServerStatusService _serverStatus;
    private readonly UpdateService _update;

    public MainWindow(
        MainViewModel viewModel,
        SessionManager sessions,
        ThemeService theme,
        WindowPlacementService placement,
        HotkeyService hotkeys,
        ServerStatusService serverStatus,
        UpdateService update)
    {
        _viewModel = viewModel;
        _sessions = sessions;
        _theme = theme;
        _placement = placement;
        _hotkeys = hotkeys;
        _serverStatus = serverStatus;
        _update = update;
        InitializeComponent();
        WindowTheming.Track(this);
        DataContext = viewModel;

        _placement.Restore(this, "main");

        _sessions.SessionCountChanged += (_, _) => Dispatcher.Invoke(OnSessionsChanged);
        _viewModel.Connections.CollectionChanged += (_, _) => UpdateEmptyHint();

        // Gorunum/yogunluk/olcek ayarlardan degisebiliyor; VM degisince yansit.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGridView)) UpdateViewMode();
            else if (e.PropertyName == nameof(MainViewModel.IsCompact)) UpdateDensity();
            else if (e.PropertyName == nameof(MainViewModel.UiScale)) UpdateUiScale();
        };

        UpdateEmptyHint();
        UpdateDensity();
        UpdateViewMode();
        UpdateUiScale();
        OnSessionsChanged();

        Activated += (_, _) => _serverStatus.RefreshNow();

        _update.UpdateAvailable += (_, _) => Dispatcher.Invoke(() =>
        {
            _viewModel.StatusText = Loc.F("Guncelleme mevcut: v{0} — Ayarlardan guncelleyebilirsiniz.", _update.LatestVersion);
        });
    }

    /// <summary>Secili baglantilar (coklu secim).</summary>
    private List<RdpConnection> SelectedConnections
        => ConnectionList.SelectedItems.OfType<RdpConnection>().ToList();

    /// <summary>
    /// Arama kutusu odakta olmasa bile yazmaya baslayinca odagi ona ver ve
    /// karakteri kutuya yaz (tarayici adres cubugu gibi "yaz-ara").
    /// </summary>
    protected override void OnPreviewTextInput(System.Windows.Input.TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);

        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

        // Zaten bir metin kutusunda yaziyorsak (arama, arama dialoglari) dokunma.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void OnSessionsChanged()
    {
        _viewModel.UpdateOpenStates(_sessions.IsOpen);
        UpdateStatus();
    }

    // ------------------------------------------------------------- baglanma

    private void ConnectionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConnectSelected();

    private void ConnectionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ConnectSelected();
        e.Handled = true;
    }

    /// <summary>Aramada tek sonuc kaldiysa Enter dogrudan ona baglanir.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var matches = _viewModel.ConnectionsView.Cast<RdpConnection>().Take(2).ToList();
        if (matches.Count == 1)
        {
            _sessions.Open(matches[0]);
            e.Handled = true;
        }
    }

    /// <summary>Secili tum baglantilari acar (coklu secimde hepsini).</summary>
    private void ConnectSelected()
    {
        foreach (var connection in SelectedConnections) _sessions.Open(connection);
    }

    private void ConnectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Tekil islemler (duzenle, cogalt) icin "birincil" secim.
        _viewModel.SelectedConnection = ConnectionList.SelectedItem as RdpConnection;

        UpdateContextMenuLabels();
        UpdateStatus();
    }

    /// <summary>Coklu secimde menu basliklari kac ogeyi etkiledigini soylesin.</summary>
    private void UpdateContextMenuLabels()
    {
        var count = ConnectionList.SelectedItems.Count;
        var many = count > 1;

        ConnectMenuItem.Header = many ? Loc.F("{0} baglantiyi ac", count) : Loc.T("Baglan");
        DeleteMenuItem.Header = many ? Loc.F("{0} baglantiyi sil", count) : Loc.T("Sil");

        // Bunlar tek kayit uzerinde anlamli.
        EditMenuItem.IsEnabled = count == 1;
        DuplicateMenuItem.IsEnabled = count == 1;

        // Favori: secimin tamami zaten favoriyse "kaldir", degilse "sabitle".
        var selected = SelectedConnections;
        var allFav = selected.Count > 0 && selected.All(c => c.IsFavorite);
        FavoriteMenuItem.Header = allFav
            ? (many ? Loc.T("Sabitlemeyi kaldir") : Loc.T("Sabiti kaldir"))
            : Loc.T("Sabitle");
        FavoriteMenuItem.IsEnabled = selected.Count > 0;
    }

    // ------------------------------------------- elle siralama (surukle-birak)

    private void ConnectionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = ItemUnder(e.OriginalSource as DependencyObject);
    }

    private void ConnectionList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;
        if (!_viewModel.CanReorder) return;

        var moved = e.GetPosition(null) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var dragged = _dragCandidate;
        _dragCandidate = null;
        try
        {
            DragDrop.DoDragDrop(ConnectionList, dragged, DragDropEffects.Move);
        }
        finally
        {
            RemoveInsertionAdorner();
        }
    }

    private void ConnectionList_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = _viewModel.CanReorder && e.Data.GetDataPresent(typeof(RdpConnection));
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        if (canDrop) UpdateInsertionAdorner(e.GetPosition(ConnectionList));
        else RemoveInsertionAdorner();
    }

    private void ConnectionList_Drop(object sender, DragEventArgs e)
    {
        RemoveInsertionAdorner();

        if (!_viewModel.CanReorder) return;
        if (e.Data.GetData(typeof(RdpConnection)) is not RdpConnection dragged) return;

        var target = ItemUnder(e.OriginalSource as DependencyObject);
        if (target is null || ReferenceEquals(target, dragged)) return;

        _viewModel.Move(dragged, _viewModel.Connections.IndexOf(target));
        e.Handled = true;
    }

    // --------------------------------------- birakma yeri gostergesi (adorner)

    private InsertionAdorner? _insertionAdorner;

    private void UpdateInsertionAdorner(Point posInList)
    {
        // Kart gorunumunde yatay ekleme cizgisi anlamsiz; surukleme yine calisir.
        if (_viewModel.IsGridView)
        {
            RemoveInsertionAdorner();
            return;
        }

        var container = ContainerUnder(posInList);
        if (container is null)
        {
            RemoveInsertionAdorner();
            return;
        }

        var top = container.TranslatePoint(new Point(0, 0), ConnectionList).Y;
        var height = container.ActualHeight;
        var insertAbove = posInList.Y < top + height / 2;
        var y = insertAbove ? top : top + height;

        _insertionAdorner ??= CreateInsertionAdorner();
        if (_insertionAdorner is null) return;

        _insertionAdorner.X1 = 16;
        _insertionAdorner.X2 = Math.Max(20, ConnectionList.ActualWidth - 16);
        _insertionAdorner.Y = y;
        _insertionAdorner.InvalidateVisual();
    }

    private InsertionAdorner? CreateInsertionAdorner()
    {
        var layer = AdornerLayer.GetAdornerLayer(ConnectionList);
        if (layer is null) return null;

        var brush = (System.Windows.Media.Brush)FindResource("Brush.Accent");
        var adorner = new InsertionAdorner(ConnectionList, brush);
        layer.Add(adorner);
        return adorner;
    }

    private void RemoveInsertionAdorner()
    {
        if (_insertionAdorner is null) return;
        AdornerLayer.GetAdornerLayer(ConnectionList)?.Remove(_insertionAdorner);
        _insertionAdorner = null;
    }

    /// <summary>Verilen noktanin (ListBox koordinati) altindaki satir kapsayicisi.</summary>
    private ListBoxItem? ContainerUnder(Point p)
    {
        var hit = VisualTreeHelper.HitTest(ConnectionList, p)?.VisualHit;
        while (hit is not null and not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);
        return hit as ListBoxItem;
    }

    /// <summary>Tiklanan gorsel ogeden hangi baglantiya ait oldugunu bulur.</summary>
    private static RdpConnection? ItemUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);

        return (source as ListBoxItem)?.DataContext as RdpConnection;
    }

    // ------------------------------------------------------- sag tik menusu

    /// <summary>
    /// Sag tik varsayilan olarak ogeyi secmiyor; menu yanlis baglanti uzerinde
    /// islem yapmasin diye imlecin altindakini once seciyoruz.
    /// </summary>
    private void ConnectionList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource as DependencyObject) is not { } connection) return;

        // Coklu secimin icine sag tiklandiysa secimi BOZMA; disina tiklandiysa
        // yalniz o ogeyi sec.
        if (!ConnectionList.SelectedItems.Contains(connection))
            ConnectionList.SelectedItem = connection;
    }

    private void ContextConnect_Click(object sender, RoutedEventArgs e) => ConnectSelected();

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedConnections;
        if (selected.Count == 0) return;

        // Karisik durumda hepsini favori yap; hepsi favoriyse hepsinden kaldir.
        var makeFav = !selected.All(c => c.IsFavorite);
        foreach (var c in selected)
        {
            if (c.IsFavorite != makeFav) _viewModel.ToggleFavorite(c);
        }
    }

    // ----------------------------------------------------------- siralama

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (SortButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = SortButton;
            menu.IsOpen = true;
        }
    }

    /// <summary>Sort menusunu butonun sag kenarina hizala (sola dogru acilsin).</summary>
    private void SortMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ContextMenu menu)
            menu.HorizontalOffset = SortButton.ActualWidth - menu.ActualWidth;
    }

    private void SortMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag } &&
            Enum.TryParse<ConnectionSortMode>(tag, out var mode))
        {
            _viewModel.SortMode = mode;
        }
    }

    // ---------------------------------------------------------------- ayarlar

    private void History_Click(object sender, RoutedEventArgs e)
        => new HistoryWindow(App.Services.GetRequiredService<ConnectionRepository>(), _sessions) { Owner = this }.Show();

    /// <summary>Ctrl+K / Ctrl+Shift+P: komut paletini komut modunda ac.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && (e.Key == Key.K || (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))))
        {
            App.Services.GetRequiredService<LauncherWindow>().ShowLauncher(">");
            e.Handled = true;
        }
    }

    /// <summary>Komut paletindeki komutlar. Diyalog acanlar once bu pencereyi one getirir.</summary>
    internal IReadOnlyList<PaletteCommand> BuildPaletteCommands()
    {
        var e = new RoutedEventArgs();
        Action OnMain(Action action) => () => { App.ShowMainWindow(); action(); };

        return
        [
            new(Loc.T("Yeni baglanti"), Loc.T("Baglanti editorunu ac"), OnMain(() => New_Click(this, e))),
            new(Loc.T("Baglanti gecmisi"), Loc.T("Son baglantilar ve sureleri"), OnMain(() => History_Click(this, e))),
            new(Loc.T("Ayarlar"), Loc.T("Gorunum, palet, guncelleme"), OnMain(() => Settings_Click(this, e))),
            new(Loc.T("Acik / koyu tema"), Loc.T("Temayi degistir"), () => _theme.Toggle()),
            new(Loc.T("Ice aktar"), Loc.T(".rdp veya .json"), OnMain(() => Import_Click(this, e))),
            new(Loc.T("Disa aktar"), Loc.T("Parolali JSON"), OnMain(() => Export_Click(this, e))),
            new(Loc.T("Sunuculari yokla"), Loc.T("Erisilebilirligi simdi kontrol et"),
                () => _ = App.Services.GetRequiredService<ReachabilityService>().CheckAllAsync()),
            new(Loc.T("Sunucu listesini goster"), Loc.T("Ana pencere"), App.ShowMainWindow),
            new(Loc.T("Tum oturumlari kapat"), Loc.T("Acik RDP sekmelerini kapatir"), () => _sessions.CloseAll()),
        ];
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var newKey = ApiKeyDialog.Show(this, _serverStatus.ApiKey, _update, _theme, _viewModel);
        if (newKey is not null)
        {
            _serverStatus.ApiKey = newKey;
            _viewModel.StatusText = string.IsNullOrEmpty(newKey)
                ? Loc.T("API anahtari kaldirildi, sunucu durumu takibi devre disi.")
                : Loc.T("API anahtari kaydedildi, sunucu durumlari takip ediliyor.");
        }
    }

    /// <summary>
    /// Satir <-> kart gorunumu. Sablon, panel ve kapsayici stili birlikte
    /// degisiyor: kart gorunumunde secim gorseli kartin kendi cercevesinde.
    /// </summary>
    private void UpdateViewMode()
    {
        var grid = _viewModel.IsGridView;

        ConnectionList.ItemTemplate = (DataTemplate)FindResource(grid ? "ConnectionCard" : "ConnectionRow");
        ConnectionList.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "RowPanel");
        ConnectionList.ItemContainerStyle = grid ? (Style)FindResource("CardContainer") : null;
    }

    private void UpdateDensity()
    {
        var compact = _viewModel.IsCompact;

        ConnectionList.Tag = compact
            ? new Thickness(14, 4, 14, 4)
            : new Thickness(14, 9, 14, 9);
    }

    private void UpdateUiScale()
    {
        UiScaleTransform.ScaleX = _viewModel.UiScale;
        UiScaleTransform.ScaleY = _viewModel.UiScale;
    }

    // ----------------------------------------------------------------- liste

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionEditorDialog(_viewModel.CreateNew(), isNew: true, _viewModel.AllTagNames) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } created && ConfirmDuplicateAddress(created))
        {
            _viewModel.Save(created);
            ConnectionList.SelectedItem = _viewModel.SelectedConnection;
        }
    }

    /// <summary>
    /// Ayni adres zaten kayitliysa onay ister.
    ///
    /// Engellemiyoruz: ayni makineye farkli kimlik bilgisi veya farkli profille
    /// (ornegin biri surucu yonlendirmeli) ayri kayit tutmak mesru bir kullanim.
    /// Amac yalnizca kazara olusan kopyalari yakalamak.
    /// </summary>
    private bool ConfirmDuplicateAddress(RdpConnection candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Address)) return true;

        var existing = _viewModel.Connections.FirstOrDefault(c =>
            !string.Equals(c.Id, candidate.Id, StringComparison.Ordinal) &&
            string.Equals(c.Address, candidate.Address, StringComparison.OrdinalIgnoreCase) &&
            c.Port == candidate.Port);

        if (existing is null) return true;

        var answer = MessageBox.Show(
            this,
            Loc.F("Bu adres '{0}' adiyla zaten kayitli.", existing.DisplayName) + "\n\n" +
            Loc.T("Farkli kimlik bilgisi veya farkli ayarlar icin ayri bir kayit tutuyorsaniz sorun yok.") + "\n\n" +
            Loc.T("Kaydedilsin mi?"),
            Loc.T("Ayni adres zaten kayitli"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return answer == MessageBoxResult.Yes;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedConnection is not { } selected)
        {
            _viewModel.StatusText = Loc.T("Once listeden bir baglanti secin.");
            return;
        }

        var dialog = new ConnectionEditorDialog(selected, isNew: false, _viewModel.AllTagNames) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } edited && ConfirmDuplicateAddress(edited))
        {
            _viewModel.Save(edited);
            ConnectionList.SelectedItem = _viewModel.SelectedConnection;
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.DuplicateSelected() is not { } copy)
        {
            _viewModel.StatusText = Loc.T("Once cogaltilacak baglantiyi secin.");
            return;
        }

        // Kopyayi dogrudan kaydetmek yerine editorde ac: kullanici adi/adresi
        // degistirmeden kaydetmesi nadiren istenen bir sey.
        var dialog = new ConnectionEditorDialog(copy, isNew: true, _viewModel.AllTagNames) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } created && ConfirmDuplicateAddress(created))
        {
            _viewModel.Save(created);
            ConnectionList.SelectedItem = _viewModel.SelectedConnection;
        }
    }

    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag })
            _viewModel.ToggleTag(tag);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedConnections;
        if (selected.Count == 0)
        {
            _viewModel.StatusText = Loc.T("Once listeden bir baglanti secin.");
            return;
        }

        var message = selected.Count == 1
            ? Loc.F("'{0}' silinsin mi?", selected[0].DisplayName)
            : Loc.F("{0} baglanti silinsin mi?", selected.Count) + "\n\n"
              + string.Join("\n", selected.Take(8).Select(c => "- " + c.DisplayName))
              + (selected.Count > 8 ? "\n" + Loc.F("... ve {0} tane daha", selected.Count - 8) : string.Empty);

        var answer = MessageBox.Show(
            this, message, Loc.T("Silme onayi"), MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        foreach (var connection in selected) _viewModel.Delete(connection);
    }

    // ------------------------------------------------------- ice / disa aktar

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Ice aktar"),
            Filter = Loc.T("Desteklenen dosyalar") + " (*.rdp;*.json)|*.rdp;*.json|"
                   + Loc.T("Uzak masaustu dosyalari") + " (*.rdp)|*.rdp|"
                   + Loc.T("RdpManager listesi") + " (*.json)|*.json",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        var imported = new List<RdpConnection>();
        var failed = new List<string>();

        foreach (var file in dialog.FileNames)
        {
            try
            {
                if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string? passphrase = null;
                    if (ConnectionJsonIO.IsEncrypted(file))
                    {
                        passphrase = PassphraseDialog.AskForImport(this);
                        if (passphrase is null) continue;
                    }
                    imported.AddRange(ConnectionJsonIO.Import(file, passphrase));
                }
                else
                {
                    imported.Add(RdpFileImporter.Import(file));
                }
            }
            catch (CryptographicException)
            {
                Logger.Warning("Yanlis parola ile ice aktarma denemesi: {File}", file);
                MessageBox.Show(this, Loc.T("Parola yanlis veya dosya bozuk."),
                    Loc.T("Ice aktarma basarisiz"), MessageBoxButton.OK, MessageBoxImage.Warning);
                failed.Add(Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Ice aktarma basarisiz: {File}", file);
                failed.Add(Path.GetFileName(file));
            }
        }

        if (imported.Count > 0) _viewModel.SaveMany(imported);

        _viewModel.StatusText = failed.Count == 0
            ? Loc.F("{0} baglanti ice aktarildi.", imported.Count)
            : Loc.F("{0} ice aktarildi, {1} basarisiz: {2}", imported.Count, failed.Count, string.Join(", ", failed));
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Connections.Count == 0)
        {
            _viewModel.StatusText = Loc.T("Disa aktarilacak baglanti yok.");
            return;
        }

        var passphrase = PassphraseDialog.AskForExport(this);
        if (passphrase is null) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Disa aktar"),
            Filter = Loc.T("RdpManager listesi") + " (*.json)|*.json",
            FileName = $"rdpmanager-{DateTime.Now:yyyy-MM-dd}.json",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ConnectionJsonIO.Export(
                dialog.FileName,
                _viewModel.Connections,
                passphrase,
                c => _viewModel.ResolvePasswordFor(c));

            var label = string.IsNullOrEmpty(passphrase) ? Loc.T("parolalar haric") : Loc.T("parolalar dahil, sifreli");
            _viewModel.StatusText = Loc.F("{0} baglanti disa aktarildi ({1}).", _viewModel.Connections.Count, label);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Disa aktarma basarisiz");
            MessageBox.Show(this, ex.Message, Loc.T("Disa aktarma basarisiz"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------- durum

    private void UpdateEmptyHint()
    {
        var isEmpty = _viewModel.Connections.Count == 0;
        EmptyHint.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        ConnectionList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateStatus()
    {
        var text = _sessions.SessionCount == 0
            ? Loc.F("{0} baglanti", _viewModel.Connections.Count)
            : Loc.F("{0} baglanti - {1} acik oturum", _viewModel.Connections.Count, _sessions.SessionCount);

        if (ConnectionList.SelectedItems.Count > 1)
            text += "     " + Loc.F("{0} secili", ConnectionList.SelectedItems.Count);

        // Hangi kisayolun tutundugu degisebiliyor; kullaniciya burada bildiriyoruz.
        if (_hotkeys.ActiveHotkey is { } hotkey) text += "     " + Loc.F("{0} - hizli baglan", hotkey);

        _viewModel.StatusText = text;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _placement.Save(this, "main");

        // 1Remote gibi: X'e basmak uygulamayi kapatmaz, tray'e ceker.
        // Gercek cikis yalniz tray menusundeki "Cikis" ile (App.IsExiting).
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Uygulama gercekten kapaniyor; acik oturumlari da kapat.
        _sessions.CloseAll();
        base.OnClosed(e);
    }
}
