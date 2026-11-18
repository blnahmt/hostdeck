using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Models;
using RdpManager.Rdp;
using RdpManager.Services;
using RdpManager.Localization;

namespace RdpManager.ViewModels;

/// <summary>
/// Bir sekme = bir oturum. Sekme pencereler arasi tasindiginda bu nesne
/// tasinir, <see cref="Host"/> yeniden yaratilmaz — oturum boylece hayatta kalir.
/// </summary>
public sealed partial class SessionTabItem : ObservableObject
{
    private readonly SessionManager _manager;

    [ObservableProperty] private RdpSessionStatus _status = RdpSessionStatus.NotInitialized;
    [ObservableProperty] private string _statusText = Loc.T("Baglaniyor...");

    /// <summary>
    /// Sekme icerigine BAGLANAN host. Tam ekrana gecerken null yapiliyor —
    /// ayni gorsel ayni anda iki yerde olamaz. Host nesnesi yok edilmiyor,
    /// sadece sekmeden cikarilip tam ekran penceresine takiliyor.
    /// </summary>
    [ObservableProperty] private ISessionHost? _displayedHost;

    public SessionTabItem(SessionManager manager, ISessionHost host)
    {
        _manager = manager;
        Host = host;
        DisplayedHost = host;

        host.StatusChanged += OnHostStatusChanged;
        host.Disconnected += OnHostDisconnected;
    }

    public ISessionHost Host { get; }

    /// <summary>Acik gecmis kaydi (bagliyken dolu, kopunca kapatilir).</summary>
    internal long? HistoryId { get; set; }

    public RdpConnection Connection => Host.Connection;

    public string Title => Connection.DisplayName;

    public bool IsConnected => Status == RdpSessionStatus.Connected;

    /// <summary>
    /// Bu sekme goruntulenmiyorken baglanti koptu ve kullanici henuz gormedi.
    /// Sekme baslıgi vurgulanir; sekmeye gecince temizlenir.
    /// </summary>
    [ObservableProperty] private bool _hasUnseenAlert;

    /// <summary>Sekme secildiginde cagrilir.</summary>
    public void MarkSeen() => HasUnseenAlert = false;

    [RelayCommand]
    private void Reconnect() => Host.Reconnect();

    [RelayCommand]
    private void SendCtrlAltDelete() => Host.SendCtrlAltDelete();

    [RelayCommand]
    private void FullScreen() => _manager.EnterFullScreen(this);

    [RelayCommand]
    private void OpenInMstsc() => _manager.OpenInMstsc(this);

    [RelayCommand]
    private void Close() => _manager.Close(this);

    [RelayCommand]
    private void Edit() => _manager.EditConnection(this);

    [RelayCommand]
    private void Duplicate() => _manager.DuplicateConnection(this);

    [RelayCommand]
    private void Delete() => _manager.DeleteConnection(this);

    /// <summary>Sekme kapanirken cagrilir; host'un dispose'u kuyruga birakilir.</summary>
    internal void DetachEvents()
    {
        Host.StatusChanged -= OnHostStatusChanged;
        Host.Disconnected -= OnHostDisconnected;
    }

    private void OnHostStatusChanged(object? sender, RdpSessionStatus status)
    {
        Status = status;
        StatusText = status switch
        {
            RdpSessionStatus.Connecting => Loc.T("Baglaniyor..."),
            RdpSessionStatus.Connected => Loc.T("Bagli"),
            RdpSessionStatus.Disconnected => Loc.T("Baglanti kesildi"),
            _ => string.Empty
        };
        OnPropertyChanged(nameof(IsConnected));
    }

    private void OnHostDisconnected(object? sender, RdpDisconnectInfo info) => StatusText = info.Message;
}
