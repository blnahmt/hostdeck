using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using RdpManager.Data;
using RdpManager.Models;
using RdpManager.Localization;

namespace RdpManager.ViewModels;

public enum ConnectionSortMode
{
    /// <summary>Kullanicinin surukleyerek belirledigi sira.</summary>
    Manual,
    Name,
    Address,
    LastUsed
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConnectionRepository _repository;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private RdpConnection? _selectedConnection;
    [ObservableProperty] private string _statusText = Loc.T("Hazir");
    [ObservableProperty] private ConnectionSortMode _sortMode = ConnectionSortMode.Manual;

    /// <summary>Liste satirlarinin sikligi. Tercih veritabaninda saklaniyor.</summary>
    [ObservableProperty] private bool _isCompact;

    /// <summary>Kart (grid) gorunumu. Tercih veritabaninda saklaniyor.</summary>
    [ObservableProperty] private bool _isGridView;

    /// <summary>Listede oturum kucuk resmi gosterilsin mi. Tercih saklaniyor.</summary>
    [ObservableProperty] private bool _showThumbnails = true;

    /// <summary>Cikista kucuk resimleri temizle. Tercih saklaniyor.</summary>
    [ObservableProperty] private bool _clearThumbnailsOnExit;

    /// <summary>Yeni baglanti icin varsayilan kullanici adi / domain.</summary>
    [ObservableProperty] private string _defaultUserName = string.Empty;
    [ObservableProperty] private string _defaultDomain = string.Empty;

    /// <summary>Arayuz olcegi (1.0 = normal). Tercih saklaniyor.</summary>
    [ObservableProperty] private double _uiScale = 1.0;

    public MainViewModel(ConnectionRepository repository)
    {
        _repository = repository;

        Connections = [];
        Tags = [];
        SelectedTags = [];
        ConnectionsView = CollectionViewSource.GetDefaultView(Connections);
        ConnectionsView.Filter = Matches;

        _isCompact = _repository.GetSetting("list.compact") == "1";
        _isGridView = _repository.GetSetting("list.grid") == "1";
        _showThumbnails = _repository.GetSetting("list.thumbnails") != "0";   // varsayilan acik
        _clearThumbnailsOnExit = _repository.GetSetting("thumb.clearOnExit") == "1";
        _defaultUserName = _repository.GetSetting("conn.default.user") ?? string.Empty;
        _defaultDomain = _repository.GetSetting("conn.default.domain") ?? string.Empty;
        _uiScale = double.TryParse(_repository.GetSetting("ui.scale"),
            System.Globalization.CultureInfo.InvariantCulture, out var sc) && sc >= 0.8 && sc <= 2.0
            ? sc : 1.0;
        _sortMode = Enum.TryParse<ConnectionSortMode>(_repository.GetSetting("list.sort"), out var sort)
            ? sort
            : ConnectionSortMode.Manual;

        Reload();
    }

    partial void OnIsCompactChanged(bool value)
        => _repository.SetSetting("list.compact", value ? "1" : "0");

    partial void OnIsGridViewChanged(bool value)
        => _repository.SetSetting("list.grid", value ? "1" : "0");

    partial void OnShowThumbnailsChanged(bool value)
        => _repository.SetSetting("list.thumbnails", value ? "1" : "0");

    partial void OnClearThumbnailsOnExitChanged(bool value)
        => _repository.SetSetting("thumb.clearOnExit", value ? "1" : "0");

    partial void OnDefaultUserNameChanged(string value)
        => _repository.SetSetting("conn.default.user", value ?? string.Empty);

    partial void OnDefaultDomainChanged(string value)
        => _repository.SetSetting("conn.default.domain", value ?? string.Empty);

    partial void OnUiScaleChanged(double value)
        => _repository.SetSetting("ui.scale", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Varsayilanlari uygulanmis yeni bir baglanti olusturur.</summary>
    public RdpConnection CreateNew()
    {
        var c = new RdpConnection();
        if (!string.IsNullOrWhiteSpace(DefaultUserName)) c.UserName = DefaultUserName.Trim();
        if (!string.IsNullOrWhiteSpace(DefaultDomain)) c.Domain = DefaultDomain.Trim();
        return c;
    }

    /// <summary>Editorde hizli secim icin listedeki tum etiketler.</summary>
    public IReadOnlyList<string> AllTagNames => [.. Tags.Select(t => t.Name)];

    public ObservableCollection<RdpConnection> Connections { get; }

    /// <summary>Listede gecen tum etiketler (cip olarak gosteriliyor).</summary>
    public ObservableCollection<TagFilterItem> Tags { get; }

    /// <summary>Secili etiketler. Bos = filtre yok.</summary>
    public ObservableCollection<string> SelectedTags { get; }

    public ICollectionView ConnectionsView { get; }

    public bool HasTags => Tags.Count > 0;

    /// <summary>Elle siralama sadece Manual modda mumkun.</summary>
    public bool CanReorder => SortMode == ConnectionSortMode.Manual;

    public void Reload()
    {
        var previousId = SelectedConnection?.Id;
        // Yeni nesnelere son yoklama sonucunu tasi; yoksa nokta bir sonraki yoklamaya kadar kaybolur.
        var reachable = Connections.ToDictionary(c => c.Id, c => c.IsReachable);

        Connections.Clear();
        // Favoriler her zaman ustte; kendi ici sirasi SortOrder'a gore.
        foreach (var item in _repository.GetAll().OrderByDescending(c => c.IsFavorite).ThenBy(c => c.SortOrder))
        {
            item.IsReachable = reachable.GetValueOrDefault(item.Id);
            Connections.Add(item);
        }

        RebuildTags();
        ApplySort();
        UpdateCountStatus();

        if (previousId is not null)
            SelectedConnection = Connections.FirstOrDefault(c => c.Id == previousId);
    }

    public void Save(RdpConnection connection)
    {
        _repository.Save(connection);
        Reload();
        SelectedConnection = Connections.FirstOrDefault(c => c.Id == connection.Id);
    }

    public void SaveMany(IEnumerable<RdpConnection> connections)
    {
        foreach (var connection in connections) _repository.Save(connection);
        Reload();
    }

    public void Delete(RdpConnection connection)
    {
        _repository.Delete(connection.Id);
        RdpManager.Services.ThumbnailStore.Delete(connection.Id);
        Reload();
    }

    /// <summary>Oturum kapaninca yeni kucuk resim kaydedildiginde listedeki resmi tazeler.</summary>
    public void RefreshThumbnail(string id)
    {
        var match = Connections.FirstOrDefault(c => c.Id == id);
        if (match is not null) match.ThumbnailVersion++;
    }

    /// <summary>Baglantiyi favori yapar/kaldirir; favoriler listede uste tasinir.</summary>
    public void ToggleFavorite(RdpConnection connection)
    {
        connection.IsFavorite = !connection.IsFavorite;
        _repository.Save(connection);
        Reload();
        SelectedConnection = Connections.FirstOrDefault(c => c.Id == connection.Id);
    }

    /// <summary>Surukle-birak sonrasi yeni sirayi uygular ve kaydeder.</summary>
    public void Move(RdpConnection dragged, int targetIndex)
    {
        if (!CanReorder) return;

        var currentIndex = Connections.IndexOf(dragged);
        if (currentIndex < 0) return;

        targetIndex = Math.Clamp(targetIndex, 0, Connections.Count - 1);
        if (currentIndex == targetIndex) return;

        Connections.Move(currentIndex, targetIndex);
        _repository.SaveOrder(Connections);
        SelectedConnection = dragged;
    }

    public string? ResolvePasswordFor(RdpConnection connection)
    {
        _repository.ResolvePassword(connection);
        var pw = connection.Password;
        connection.Password = null;
        return pw;
    }

    public IReadOnlyList<RdpConnection> AllConnections => Connections;

    /// <summary>Secili baglantiyi cogaltir ve kopyayi dondurur (kaydedilmeden).</summary>
    public RdpConnection? DuplicateSelected() => SelectedConnection?.Duplicate();

    /// <summary>
    /// Hangi baglantilarin acik oturumu oldugunu isaretler.
    /// SessionManager oturum sayisi degistikce cagiriyor.
    /// </summary>
    public void UpdateOpenStates(Func<string, bool> isOpen)
    {
        foreach (var connection in Connections)
            connection.IsSessionOpen = isOpen(connection.Id);
    }

    // ---------------------------------------------------------- etiket cipleri

    public void ToggleTag(string tag)
    {
        if (!SelectedTags.Remove(tag)) SelectedTags.Add(tag);

        foreach (var item in Tags) item.IsSelected = SelectedTags.Contains(item.Name);

        ConnectionsView.Refresh();
        UpdateCountStatus();
    }

    public void ClearTagFilter()
    {
        SelectedTags.Clear();
        foreach (var item in Tags) item.IsSelected = false;

        ConnectionsView.Refresh();
        UpdateCountStatus();
    }

    private void RebuildTags()
    {
        var names = Connections
            .SelectMany(c => c.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Artik var olmayan etiketler secili kalmasin.
        foreach (var stale in SelectedTags.Where(t => !names.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList())
            SelectedTags.Remove(stale);

        Tags.Clear();
        foreach (var name in names)
            Tags.Add(new TagFilterItem(name) { IsSelected = SelectedTags.Contains(name) });

        OnPropertyChanged(nameof(HasTags));
    }

    partial void OnSearchTextChanged(string value)
    {
        ConnectionsView.Refresh();
        UpdateCountStatus();
    }

    partial void OnSortModeChanged(ConnectionSortMode value)
    {
        _repository.SetSetting("list.sort", value.ToString());
        ApplySort();
        OnPropertyChanged(nameof(CanReorder));
    }

    /// <summary>
    /// Manual modda hicbir siralama tanimlanmaz — koleksiyonun kendi sirasi
    /// (SortOrder'a gore yuklenmis hali) gecerli olur.
    /// </summary>
    private void ApplySort()
    {
        ConnectionsView.SortDescriptions.Clear();

        // Manual disindaki modlarda da favoriler ustte kalir.
        if (SortMode != ConnectionSortMode.Manual)
            ConnectionsView.SortDescriptions.Add(
                new SortDescription(nameof(RdpConnection.IsFavorite), ListSortDirection.Descending));

        switch (SortMode)
        {
            case ConnectionSortMode.Name:
                ConnectionsView.SortDescriptions.Add(
                    new SortDescription(nameof(RdpConnection.Name), ListSortDirection.Ascending));
                break;

            case ConnectionSortMode.Address:
                ConnectionsView.SortDescriptions.Add(
                    new SortDescription(nameof(RdpConnection.Address), ListSortDirection.Ascending));
                break;

            case ConnectionSortMode.LastUsed:
                ConnectionsView.SortDescriptions.Add(
                    new SortDescription(nameof(RdpConnection.LastConnectedAt), ListSortDirection.Descending));
                break;
        }

        ConnectionsView.Refresh();
    }

    private void UpdateCountStatus()
    {
        var visible = ConnectionsView.Cast<object>().Count();
        StatusText = visible == Connections.Count
            ? Loc.F("{0} baglanti", Connections.Count)
            : Loc.F("{0} / {1} baglanti", visible, Connections.Count);
    }

    /// <summary>
    /// Etiket filtresi VE arama birlikte uygulanir.
    /// Birden fazla etiket secilirse "herhangi biri" (VEYA) mantigi kullanilir —
    /// boylece ikinci etikete tiklamak listeyi genisletir, bosaltmaz.
    /// </summary>
    private bool Matches(object item)
    {
        if (item is not RdpConnection connection) return false;

        if (SelectedTags.Count > 0 &&
            !connection.Tags.Any(tag => SelectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return connection.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || connection.Address.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || connection.Tags.Any(tag => tag.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Etiket filtresi cipi.</summary>
public sealed partial class TagFilterItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public TagFilterItem(string name) => Name = name;

    public string Name { get; }
}
