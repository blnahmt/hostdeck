using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Models;

namespace RdpManager.ViewModels;

/// <summary>
/// Editorun uzerinde calistigi KOPYA. Kaydet'e basilmadan orijinal degismez.
/// </summary>
public sealed partial class ConnectionEditorViewModel : ObservableObject
{
    /// <summary>Renk seridi secenekleri. null = renk yok.</summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "#D64545", "#E08A3C", "#D9B441", "#4FA363",
        "#3C8FA8", "#4A6FB5", "#8A5CB8", "#8A8A93",
    ];

    [ObservableProperty] private string _newTag = string.Empty;
    [ObservableProperty] private string? _selectedColor;

    public ConnectionEditorViewModel(RdpConnection connection, IEnumerable<string>? knownTags = null)
    {
        Draft = connection.Clone();
        Tags = new ObservableCollection<string>(Draft.Tags);
        SelectedColor = Draft.Color;
        HadPassword = Draft.HasPassword;

        KnownTags = [];
        foreach (var name in knownTags ?? [])
            KnownTags.Add(new KnownTag(name) { IsSelected = ContainsTag(name) });
    }

    /// <summary>
    /// Listede kullanilan tum etiketler. Tiklayarak eklenip cikarilabiliyor —
    /// her seferinde elle yazmak yerine.
    /// </summary>
    public ObservableCollection<KnownTag> KnownTags { get; }

    public bool HasKnownTags => KnownTags.Count > 0;

    public RdpConnection Draft { get; }

    public ObservableCollection<string> Tags { get; }

    public bool HadPassword { get; }

    public IReadOnlyList<string> ColorOptions => Palette;

    [RelayCommand]
    private void AddTag()
    {
        var tag = NewTag.Trim();
        if (tag.Length == 0) return;

        // Ayni etiket iki kez eklenmesin (buyuk/kucuk harf farki dahil).
        if (!ContainsTag(tag)) Tags.Add(tag);

        SyncKnownTags();
        NewTag = string.Empty;
    }

    [RelayCommand]
    private void RemoveTag(string? tag)
    {
        if (tag is null) return;

        Tags.Remove(tag);
        SyncKnownTags();
    }

    /// <summary>Mevcut etiket cipine tiklamak onu ekler veya cikarir.</summary>
    [RelayCommand]
    private void ToggleKnownTag(string? name)
    {
        if (name is null) return;

        var existing = Tags.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Tags.Remove(existing);
        else Tags.Add(name);

        SyncKnownTags();
    }

    private bool ContainsTag(string tag)
        => Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase));

    private void SyncKnownTags()
    {
        foreach (var known in KnownTags) known.IsSelected = ContainsTag(known.Name);
    }

    [RelayCommand]
    private void ClearColor() => SelectedColor = null;

    /// <summary>Editordeki degerleri taslaga yazip dondurur.</summary>
    public RdpConnection Commit(string? plainPassword, bool clearPassword)
    {
        Draft.Tags = [.. Tags];
        Draft.Color = SelectedColor;

        if (clearPassword)
        {
            // Repository sadece Password null degilse yeniden sifreler;
            // ikisini de temizleyince kayitli parola gercekten silinir.
            Draft.Password = null;
            Draft.ProtectedPassword = null;
        }
        else
        {
            // Bos birakildiysa mevcut korunmus parolaya dokunma.
            Draft.Password = string.IsNullOrEmpty(plainPassword) ? null : plainPassword;
        }

        return Draft;
    }
}

/// <summary>Editordeki "mevcut etiketler" cipi.</summary>
public sealed partial class KnownTag : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public KnownTag(string name) => Name = name;

    public string Name { get; }
}
