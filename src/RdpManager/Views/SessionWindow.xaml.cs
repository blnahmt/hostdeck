using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Dragablz;
using RdpManager.Models;
using RdpManager.Rdp;
using RdpManager.Services;
using RdpManager.ViewModels;
using RdpManager.Localization;

using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RdpManager.Views;

/// <summary>
/// Oturumlarin yasadigi pencere. Sunucu listesinden ayridir.
/// Sekme suruklenerek koparilirsa Dragablz bunun bir kopyasini yaratir.
/// </summary>
public partial class SessionWindow : ChromeWindow, ITabHostWindow
{
    private readonly SessionManager _manager;
    private readonly WindowPlacementService _placement;
    private readonly ObservableCollection<SessionTabItem> _tabs = [];

    private readonly ServerStatusService? _serverStatus =
        App.Services?.GetService(typeof(ServerStatusService)) as ServerStatusService;

    public SessionWindow(SessionManager manager, WindowPlacementService placement)
    {
        _manager = manager;
        _placement = placement;
        InitializeComponent();
        WindowTheming.Track(this);

        // Yeni sunucu durumu geldiginde acik hizli baglanti listesini guncelle.
        if (_serverStatus is not null)
            _serverStatus.StatusChanged += OnServerStatusChanged;

        // Ilk acilista tam ekran (maximize); sonraki acilislarda son boyut.
        _placement.Restore(this, "session", maximizeByDefault: true);

        SessionTabs.ItemsSource = _tabs;
        SessionTabs.InterTabController = new InterTabController
        {
            Partition = SessionManager.TabPartition,
            InterTabClient = manager.InterTabClient,
        };
        SessionTabs.ClosingItemCallback = OnClosingItem;
        SessionTabs.SelectionChanged += (_, _) =>
        {
            CurrentTab?.MarkSeen();   // sekmeye gecildi, uyari isareti kalksin
            UpdateActionButtons();
        };
        _tabs.CollectionChanged += OnTabsChanged;
        Activated += (_, _) => manager.NotifyWindowActivated(this);

        SizeChanged += (_, _) => UpdateTabWidth();

        manager.RegisterTabHost(_tabs, this);
        UpdateActionButtons();
    }

    internal TabablzControl TabControl => SessionTabs;

    public void SelectTab(SessionTabItem tab) => SessionTabs.SelectedItem = tab;

    private SessionTabItem? CurrentTab => SessionTabs.SelectedItem as SessionTabItem;

    // ---------------------------------------------------------- tab eylemleri

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is { } tab) _manager.EnterFullScreen(tab);
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e) => CurrentTab?.Host.Reconnect();

    /// <summary>
    /// Sekme genisligini pencereye ve sekme sayisina gore hesaplar
    /// (tarayicilardaki gibi: sekme cogaldikca daralir).
    /// Deger TabablzControl.Tag'e yaziliyor, DragablzItem stili oradan okuyor.
    /// </summary>
    private void UpdateTabWidth()
    {
        if (_tabs.Count == 0) return;

        // "+" butonu, eylem ikonlari ve pencere dugmeleri icin ayrilan pay.
        const double Reserved = 300;
        const double Max = 164;
        const double Min = 82;

        var available = Math.Max(SessionTabs.ActualWidth - Reserved, Min);
        var perTab = Math.Clamp(available / _tabs.Count, Min, Max);

        SessionTabs.Tag = perTab;
    }

    private void OnClosingItem(ItemActionCallbackArgs<TabablzControl> args)
    {
        if (args.DragablzItem.DataContext is SessionTabItem tab)
            _manager.OnTabClosedByUi(tab);
    }

    /// <summary>
    /// Sekmelerin durum degisikliklerini dinlemek icin: baslik yalnizca sekme
    /// eklenip cikarildiginda guncellenirse "Baglaniyor..." yazisi bagli
    /// olunduktan sonra da ekranda kalir.
    /// </summary>
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var tab in e.OldItems?.OfType<SessionTabItem>() ?? [])
            tab.PropertyChanged -= OnTabPropertyChanged;

        foreach (var tab in e.NewItems?.OfType<SessionTabItem>() ?? [])
        {
            tab.PropertyChanged += OnTabPropertyChanged;
            tab.Host.Disconnected += (_, info) => OnHostDisconnected(tab, info);
        }

        UpdateActionButtons();
    }

    /// <summary>
    /// Arka plandaki bir sekmenin baglantisi koptuysa kullaniciya belli et:
    /// sekme basligi vurgulanir, pencere one degilse gorev cubugu yanip soner.
    /// </summary>
    private void OnHostDisconnected(SessionTabItem tab, RdpDisconnectInfo info)
    {
        if (info.IsUserInitiated) return;

        Dispatcher.Invoke(() =>
        {
            var isCurrent = ReferenceEquals(tab, CurrentTab);
            if (!isCurrent) tab.HasUnseenAlert = true;

            if (!IsActive)
            {
                NativeMethods.FlashTaskbar(new WindowInteropHelper(this).Handle);

                // Kullanici baska uygulamadayken de fark etsin: Windows bildirimi.
                (App.Services?.GetService(typeof(TrayIconService)) as TrayIconService)?
                    .Notify(Loc.T("Oturum baglantisi kesildi"), $"{tab.Title}: {info.Message}");
            }
        });
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionTabItem.StatusText) or nameof(SessionTabItem.Status)
            && ReferenceEquals(sender, CurrentTab))
        {
            UpdateTitle();
        }
    }

    private void UpdateActionButtons()
    {
        var hasTab = CurrentTab is not null;
        FullScreenButton.IsEnabled = hasTab;
        ReconnectButton.IsEnabled = hasTab;

        UpdateTabWidth();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        if (CurrentTab is not { } tab)
        {
            Title = Loc.T("Oturumlar");
            return;
        }

        // Bagliyken durum yazmaya gerek yok; sadece sorunlu durumlari goster.
        Title = tab.Status == RdpSessionStatus.Connected
            ? tab.Title
            : $"{tab.Title} - {tab.StatusText}";
    }

    // ------------------------------------------------------- hizli baglanti

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        QuickSearch.Text = string.Empty;
        RefreshQuickList();
        QuickConnectPopup.IsOpen = true;
    }

    private void QuickConnectPopup_Opened(object sender, EventArgs e)
    {
        _serverStatus?.RefreshNow();
        QuickSearch.Focus();
        Keyboard.Focus(QuickSearch);
    }

    private void OnServerStatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (QuickConnectPopup.IsOpen && QuickList.ItemsSource is IEnumerable<RdpConnection> items)
                _serverStatus?.ApplyTo(items);
        });
    }

    private void QuickSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshQuickList();

    private void RefreshQuickList()
    {
        var search = QuickSearch.Text.Trim();

        var items = _manager.GetConnections()
            .Where(c => !_manager.IsOpen(c.Id))     // zaten acik olanlari gosterme
            .Where(c => string.IsNullOrEmpty(search) || Matches(c, search))
            // En son kullanilan uste: hizli acmada aranan genellikle odur.
            .OrderByDescending(c => c.LastConnectedAt ?? 0)
            .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _serverStatus?.ApplyTo(items);
        QuickList.ItemsSource = items;
        QuickList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QuickEmpty.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Count > 0) QuickList.SelectedIndex = 0;
    }

    private static bool Matches(RdpConnection connection, string search)
        => connection.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
           || connection.Address.Contains(search, StringComparison.OrdinalIgnoreCase)
           || connection.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase));

    private void QuickSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ConnectSelectedQuickItem();
                e.Handled = true;
                break;

            case Key.Down:
                QuickList.Focus();   // arama kutusundan listeye gec
                e.Handled = true;
                break;

            case Key.Escape:
                QuickConnectPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void QuickList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConnectSelectedQuickItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            QuickConnectPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void QuickList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConnectSelectedQuickItem();

    private void ConnectSelectedQuickItem()
    {
        if (QuickList.SelectedItem is not RdpConnection connection) return;

        QuickConnectPopup.IsOpen = false;
        _manager.Open(connection);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _placement.Save(this, "session");
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_serverStatus is not null)
            _serverStatus.StatusChanged -= OnServerStatusChanged;

        // Pencere kapatilirsa icindeki oturumlar da kapanmali.
        foreach (var tab in _tabs.ToList()) _manager.OnTabClosedByUi(tab);

        _manager.UnregisterTabHost(_tabs);
        base.OnClosed(e);
    }
}
