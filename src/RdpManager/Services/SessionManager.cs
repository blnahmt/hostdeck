using System.Collections.ObjectModel;
using System.Windows;
using Dragablz;
using RdpManager.Data;
using RdpManager.Localization;
using RdpManager.Models;
using RdpManager.Rdp;
using RdpManager.ViewModels;
using RdpManager.Views;
using Serilog;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager.Services;

/// <summary>
/// Tum oturumlarin tek sahibi.
///
/// Pencere modeli: sunucu listesi ile oturumlar AYRI pencerelerde.
/// Ilk oturum acildiginda bir oturum penceresi yaratilir; sonraki oturumlar
/// ayni pencereye sekme olarak eklenir. Sekme suruklenerek koparilirsa
/// yeni bir oturum penceresi olusur.
///
/// Kurallar:
///  1. Ayni baglanti iki kez acilmaz — mevcut sekme one getirilir.
///  2. RdpHost ASLA yeniden yaratilmaz; sekme/tam ekran gecislerinde re-parent edilir.
///  3. Host'lar hemen dispose edilmez, HostDisposeQueue'ya birakilir.
/// </summary>
public sealed class SessionManager
{
    public const string TabPartition = "RdpManagerSessions";

    private static readonly ILogger Logger = Log.ForContext<SessionManager>();

    private readonly HostDisposeQueue _disposeQueue;
    private readonly MstscLauncher _mstscLauncher;
    private readonly ConnectionRepository _repository;

    /// <summary>connectionId -> acik oturum. "Ayni sunucuyu iki kez acma" kurali icin.</summary>
    private readonly Dictionary<string, SessionTabItem> _sessions = [];

    private readonly Dictionary<SessionTabItem, FullScreenWindow> _fullScreenWindows = [];

    /// <summary>Acik her oturum penceresi ve sekme koleksiyonu.</summary>
    private readonly List<(ObservableCollection<SessionTabItem> Tabs, Window Owner)> _tabHosts = [];

    private Window? _lastActiveSessionWindow;

    private readonly WindowPlacementService _placement;

    public SessionManager(
        HostDisposeQueue disposeQueue,
        MstscLauncher mstscLauncher,
        ConnectionRepository repository,
        WindowPlacementService placement)
    {
        _disposeQueue = disposeQueue;
        _mstscLauncher = mstscLauncher;
        _repository = repository;
        _placement = placement;
        InterTabClient = new SessionInterTabClient(this);
    }

    /// <summary>Oturum penceresi uretir (koparilan sekmeler icin de kullaniliyor).</summary>
    internal SessionWindow CreateSessionWindow() => new(this, _placement);

    public IInterTabClient InterTabClient { get; }

    public event EventHandler? SessionCountChanged;

    public int SessionCount => _sessions.Count;

    /// <summary>Hizli baglanti listesi icin kayitli baglantilar.</summary>
    public IReadOnlyList<RdpConnection> GetConnections() => _repository.GetAll();

    public bool IsOpen(string connectionId) => _sessions.ContainsKey(connectionId);

    // ------------------------------------------------------------------ acma

    public void Open(RdpConnection stored)
    {
        // Kural 1: zaten acikssa yenisini acma, mevcudu one getir.
        if (_sessions.TryGetValue(stored.Id, out var existing))
        {
            Activate(existing);
            return;
        }

        if (!ConfirmSessionConflict(stored)) return;

        // Parola sadece burada, baglanma aninda cozuluyor.
        var prepared = stored.Clone();
        _repository.ResolvePassword(prepared);

        ISessionHost host = prepared.IsVnc ? new VncHost() : prepared.IsSsh ? new SshHost() : new RdpHost();
        host.Initialize(prepared);

        var tab = new SessionTabItem(this, host);
        _sessions[prepared.Id] = tab;

        // "Son kullanim" damgasi + baglandiktan birkac saniye sonra kucuk resim
        // (kapaninca yakalama bazi RDP surumlerinde ise yaramadigi icin garanti).
        host.Connected += (_, _) =>
        {
            _repository.MarkConnected(stored.Id);
            tab.HistoryId ??= _repository.StartHistory(stored);
            ScheduleThumbnail(tab);
        };
        // Otomatik yeniden baglanmada her kopma bir kaydi kapatir, her baglanti yenisini acar.
        host.Disconnected += (_, _) => EndHistory(tab);

        var window = GetOrCreateSessionWindow();
        FindTabs(window)?.Add(tab);

        window.Show();
        window.Activate();
        if (window is ITabHostWindow tabHost) tabHost.SelectTab(tab);

        Logger.Information("Oturum acildi: {Conn} (toplam {Count})", prepared.DisplayName, _sessions.Count);
        SessionCountChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ayni makineye ayni kullanici adiyla ikinci oturum aciliyorsa uyarir.
    ///
    /// Neden: "ayni baglantiyi iki kez acma" kurali kayit KIMLIGINE bakiyor.
    /// Ayni sunucuya isaret eden iki ayri kayit varsa ikisi de acilabilir ve
    /// Windows, ayni kullanici icin ikinci oturum kurulurken birincisini
    /// dusurur — kullanici farkinda olmadan kendi oturumunu kapatir.
    /// </summary>
    private bool ConfirmSessionConflict(RdpConnection candidate)
    {
        // Yalniz RDP'de ayni kullanicinin ikinci oturumu birincisini dusurur.
        if (!candidate.IsRdp) return true;

        // Kullanici adi bossa Windows soracak; hangi hesapla girilecegi belirsiz.
        if (string.IsNullOrWhiteSpace(candidate.UserName)) return true;

        var conflict = _sessions.Values.FirstOrDefault(tab =>
            !string.Equals(tab.Connection.Id, candidate.Id, StringComparison.Ordinal) &&
            string.Equals(tab.Connection.Address, candidate.Address, StringComparison.OrdinalIgnoreCase) &&
            tab.Connection.Port == candidate.Port &&
            string.Equals(tab.Connection.UserName, candidate.UserName, StringComparison.OrdinalIgnoreCase));

        if (conflict is null) return true;

        var answer = MessageBox.Show(
            FindOwnerWindow(conflict) ?? Application.Current.MainWindow,
            Loc.F("'{0}' sekmesinde {1} makinesinde '{2}' kullanicisiyla zaten acik bir oturum var.\n\nWindows ayni kullanici icin ikinci oturum acildiginda mevcut oturumu dusurebilir.\n\nYine de acilsin mi?",
                conflict.Title, candidate.Address, candidate.UserName),
            Loc.T("Ayni makinede acik oturum"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return answer == MessageBoxResult.Yes;
    }

    public void Activate(SessionTabItem tab)
    {
        if (_fullScreenWindows.TryGetValue(tab, out var fullScreen))
        {
            fullScreen.Activate();
            return;
        }

        var owner = FindOwnerWindow(tab);
        owner?.Activate();
        if (owner is ITabHostWindow tabHost) tabHost.SelectTab(tab);
    }

    private Window GetOrCreateSessionWindow()
    {
        // En son kullanilan oturum penceresi hala acikssa oraya ekle.
        if (_lastActiveSessionWindow is not null && _tabHosts.Any(h => h.Owner == _lastActiveSessionWindow))
            return _lastActiveSessionWindow;

        if (_tabHosts.Count > 0) return _tabHosts[0].Owner;

        // SessionWindow yapicisinda RegisterTabHost cagriliyor.
        return CreateSessionWindow();
    }

    internal void NotifyWindowActivated(Window window)
    {
        if (_tabHosts.Any(h => h.Owner == window)) _lastActiveSessionWindow = window;
    }

    // --------------------------------------------------------------- kapatma

    public void Close(SessionTabItem tab)
    {
        FindCollection(tab)?.Remove(tab);
        Cleanup(tab);
    }

    /// <summary>Dragablz'in kendisi kaldirdigi sekmeler icin.</summary>
    internal void OnTabClosedByUi(SessionTabItem tab) => Cleanup(tab);

    private void Cleanup(SessionTabItem tab)
    {
        if (!_sessions.Remove(tab.Connection.Id)) return;

        if (_fullScreenWindows.Remove(tab, out var fullScreen))
        {
            fullScreen.DetachHost();
            fullScreen.Close();
        }

        // Kapatmadan once son goruntuyu yakala (hala bagli ve gorunurken).
        if (MainVm?.ShowThumbnails == true && tab.Host.CaptureThumbnail())
            MainVm.RefreshThumbnail(tab.Connection.Id);

        EndHistory(tab);
        tab.DetachEvents();
        tab.DisplayedHost = null;
        tab.Host.Disconnect();
        _disposeQueue.Enqueue(tab.Host);

        Logger.Information("Oturum kapatildi: {Conn} (kalan {Count})", tab.Title, _sessions.Count);
        SessionCountChanged?.Invoke(this, EventArgs.Empty);

        CloseEmptySessionWindows();
    }

    private void EndHistory(SessionTabItem tab)
    {
        if (tab.HistoryId is not { } id) return;
        _repository.EndHistory(id);
        tab.HistoryId = null;
    }

    /// <summary>
    /// Son sekmesi kapatilan oturum penceresi bos kalmasin.
    /// Dragablz yalniz sekme SURUKLENEREK bosaltildiginda pencereyi kapatiyor;
    /// kapat dugmesiyle bosalan pencereyi burada kapatiyoruz.
    /// </summary>
    private void CloseEmptySessionWindows()
    {
        foreach (var (tabs, owner) in _tabHosts.ToList())
        {
            if (tabs.Count == 0 && owner is SessionWindow window) window.Close();
        }
    }

    public void CloseAll()
    {
        foreach (var tab in _sessions.Values.ToList()) Close(tab);

        foreach (var (_, owner) in _tabHosts.ToList())
        {
            if (owner is SessionWindow window) window.Close();
        }
    }

    // ------------------------------------------------------------- tam ekran

    /// <summary>
    /// Host'u tam ekran penceresine TASIR (yeniden yaratmaz) — oturum kopmaz.
    /// </summary>
    public void EnterFullScreen(SessionTabItem tab)
    {
        if (_fullScreenWindows.ContainsKey(tab)) return;

        var owner = FindOwnerWindow(tab);
        var window = new FullScreenWindow(tab, this);
        _fullScreenWindows[tab] = window;

        // Once sekmeden cikar, sonra tam ekrana koy: ayni gorsel iki yerde olamaz.
        tab.DisplayedHost = null;
        window.AttachHost(tab.Host, owner);
        window.Show();
        window.Activate();

        Logger.Debug("Tam ekran: {Conn}", tab.Title);
    }

    public void ExitFullScreen(SessionTabItem tab)
    {
        if (!_fullScreenWindows.Remove(tab, out var window)) return;

        window.DetachHost();
        tab.DisplayedHost = tab.Host;
        window.Close();

        Activate(tab);
        Logger.Debug("Tam ekrandan cikildi: {Conn}", tab.Title);
    }

    /// <summary>Baglantidan birkac saniye sonra bir kez kucuk resim yakalar.</summary>
    private void ScheduleThumbnail(SessionTabItem tab)
    {
        if (MainVm?.ShowThumbnails != true) return;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (tab.Host.CaptureThumbnail())
                MainVm?.RefreshThumbnail(tab.Connection.Id);
        };
        timer.Start();
    }

    // ------------------------------------- baglanti duzenleme (sekme sag tik)

    private static MainViewModel? MainVm
        => App.Services?.GetService(typeof(MainViewModel)) as MainViewModel;

    private RdpConnection? LoadStored(string id)
        => _repository.GetAll().FirstOrDefault(c => c.Id == id);

    /// <summary>Sekmedeki baglantinin kayitli halini editorde acar ve kaydeder.</summary>
    public void EditConnection(SessionTabItem tab)
    {
        var owner = FindOwnerWindow(tab);
        var stored = LoadStored(tab.Connection.Id);
        if (stored is null)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                Loc.T("Bu baglanti artik kayitli degil."), Loc.T("Duzenle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var vm = MainVm;
        var dialog = new ConnectionEditorDialog(stored, isNew: false, vm?.AllTagNames ?? []) { Owner = owner };
        if (dialog.ShowDialog() == true && dialog.Result is { } edited)
        {
            if (vm is not null) vm.Save(edited);
            else _repository.Save(edited);
        }
    }

    /// <summary>Sekmedeki baglantiyi cogaltir (editorde acilir).</summary>
    public void DuplicateConnection(SessionTabItem tab)
    {
        var owner = FindOwnerWindow(tab);
        var stored = LoadStored(tab.Connection.Id);
        if (stored is null) return;

        var copy = stored.Duplicate();
        var vm = MainVm;
        var dialog = new ConnectionEditorDialog(copy, isNew: true, vm?.AllTagNames ?? []) { Owner = owner };
        if (dialog.ShowDialog() == true && dialog.Result is { } created)
        {
            if (vm is not null) vm.Save(created);
            else _repository.Save(created);
        }
    }

    /// <summary>Sekmedeki baglantiyi siler. Acik oturum kapanmaz.</summary>
    public void DeleteConnection(SessionTabItem tab)
    {
        var owner = FindOwnerWindow(tab);
        var answer = MessageBox.Show(
            owner ?? Application.Current.MainWindow,
            Loc.F("'{0}' baglantisi silinsin mi?\n\nAcik oturum kapanmaz.", tab.Title),
            Loc.T("Silme onayi"), MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        _repository.Delete(tab.Connection.Id);
        MainVm?.Reload();
    }

    // ------------------------------------------------------------ mstsc modu

    public void OpenInMstsc(SessionTabItem tab)
    {
        if (!tab.Connection.IsRdp) return; // mstsc yalniz RDP acar
        var layout = MonitorService.DescribeLayout();
        var mixedDpi = MonitorService.HasMixedDpi();

        var message = mixedDpi
            ? Loc.F("Monitorlerinizin olcekleri farkli ({0}).\n\nGomulu RDP kontrolu bu durumda coklu monitor tam ekrani dogru cizemiyor, bu yuzden oturum mstsc.exe ile ayri bir pencerede acilacak.\n\nNot: mstsc modunda RD Gateway calismaz.\n\nDevam edilsin mi?", layout)
            : Loc.F("Oturum mstsc.exe ile tum monitorlerde ({0}) acilacak.\n\nBu oturum sekmeli pencerenin disinda, ayri bir mstsc penceresi olur.\n\nDevam edilsin mi?", layout);

        var answer = MessageBox.Show(
            FindOwnerWindow(tab) ?? Application.Current.MainWindow,
            message, Loc.T("Coklu monitor"), MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        _mstscLauncher.Launch(tab.Connection, useMultimon: true);
    }

    // -------------------------------------------------- sekme koleksiyonlari

    internal void RegisterTabHost(ObservableCollection<SessionTabItem> tabs, Window owner)
    {
        if (_tabHosts.Any(h => h.Tabs == tabs)) return;
        _tabHosts.Add((tabs, owner));
        _lastActiveSessionWindow = owner;
    }

    internal void UnregisterTabHost(ObservableCollection<SessionTabItem> tabs)
    {
        var entry = _tabHosts.FirstOrDefault(h => h.Tabs == tabs);
        if (entry.Owner == _lastActiveSessionWindow) _lastActiveSessionWindow = null;
        _tabHosts.RemoveAll(h => h.Tabs == tabs);
    }

    private ObservableCollection<SessionTabItem>? FindTabs(Window owner)
        => _tabHosts.FirstOrDefault(h => h.Owner == owner).Tabs;

    private ObservableCollection<SessionTabItem>? FindCollection(SessionTabItem tab)
        => _tabHosts.FirstOrDefault(h => h.Tabs.Contains(tab)).Tabs;

    private Window? FindOwnerWindow(SessionTabItem tab)
    {
        foreach (var (tabs, owner) in _tabHosts)
        {
            if (tabs.Contains(tab)) return owner;
        }
        return null;
    }
}

/// <summary>Sekme barindiran pencereler.</summary>
public interface ITabHostWindow
{
    void SelectTab(SessionTabItem tab);
}
