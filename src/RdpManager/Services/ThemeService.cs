using System.Windows;
using System.Windows.Media;
using Dapper;
using Microsoft.Win32;
using RdpManager.Data;
using Serilog;

using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace RdpManager.Services;

public enum AppTheme
{
    /// <summary>Windows'un acik/koyu ayarini takip et.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Tema sozlugunu calisma aninda degistirir.
/// Tum renkler DynamicResource ile cozuldugu icin arayuz aninda guncellenir.
/// </summary>
public sealed class ThemeService
{
    private static readonly ILogger Logger = Log.ForContext<ThemeService>();
    private const string SettingKey = "theme";
    private const string AccentKey = "theme.accent";
    private const string PaletteKey = "theme.palette";

    /// <summary>Secilebilir paletler. Hostdeck: beyaz/siyah + yumusak kirmizi. Klasik: gri + kirmizi.</summary>
    public static readonly string[] Palettes = ["Hostdeck", "Klasik"];

    /// <summary>Aktif paletin ana rengi (acik tema tonu). Ayarlardan degistirilebilir.</summary>
    public string DefaultAccent => IsHostdeck ? "#E35D5B" : "#D64545";

    private bool IsHostdeck => Palette == "Hostdeck";

    private Uri LightUri => new(IsHostdeck ? "Resources/Themes/HostdeckLight.xaml" : "Resources/Themes/Light.xaml", UriKind.Relative);
    private Uri DarkUri => new(IsHostdeck ? "Resources/Themes/HostdeckDark.xaml" : "Resources/Themes/Dark.xaml", UriKind.Relative);

    private readonly Database _database;
    private ResourceDictionary? _current;

    public ThemeService(Database database) => _database = database;

    public AppTheme Theme { get; private set; } = AppTheme.System;

    public bool IsDarkActive { get; private set; }

    /// <summary>Aktif palet adi (<see cref="Palettes"/>). Varsayilan surume gore degisir.</summary>
    public string Palette { get; private set; } = AppEdition.DefaultPalette;

    /// <summary>Kullanicinin sectigi ana renk (hex); null ise tema varsayilani.</summary>
    public string? AccentColor { get; private set; }

    public event EventHandler? ThemeChanged;

    public void Initialize()
    {
        AccentColor = LoadAccent();
        var palette = LoadSetting(PaletteKey);
        if (palette == "Hostline") palette = "Hostdeck";   // eski ad (1.3.0 ve oncesi)
        Palette = Palettes.Contains(palette) ? palette! : AppEdition.DefaultPalette;
        Theme = Load();
        Apply(Theme, persist: false);

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Theme == AppTheme.System)
                Application.Current?.Dispatcher.Invoke(() => Apply(AppTheme.System, persist: false));
        };
    }

    public void Apply(AppTheme theme, bool persist = true)
    {
        Theme = theme;
        IsDarkActive = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsWindowsUsingDarkTheme()
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = IsDarkActive ? DarkUri : LightUri };

        // DIKKAT: WPF'te birlestirilmis sozluklerde AYNI anahtar icin SONRAKI kazanir.
        // Bu yuzden yeni temayi eklemek yetmez — App.xaml'deki tasarim zamani
        // tema sozlugunun YERINE gecmesi gerekiyor, yoksa o eskisini geri getirir.
        var existing = _current is not null && dictionaries.Contains(_current)
            ? _current
            : dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Themes/") == true);

        if (existing is not null) dictionaries[dictionaries.IndexOf(existing)] = replacement;
        else dictionaries.Insert(0, replacement);

        _current = replacement;

        ApplyAccent();

        // Baslik cubugu WPF'in disinda, DWM tarafindan ciziliyor - ayrica bildirilmeli.
        WindowTheming.IsDark = IsDarkActive;
        WindowTheming.RefreshAll();

        if (persist) Save(theme);
        Logger.Information("Tema uygulandi: {Theme} (koyu={Dark})", theme, IsDarkActive);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ana rengi degistirir (null = tema varsayilanina don).</summary>
    public void SetAccent(string? hex)
    {
        AccentColor = string.IsNullOrWhiteSpace(hex) ? null : hex.Trim();
        SaveAccent(AccentColor);
        ApplyAccent();
        WindowTheming.RefreshAll();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        Logger.Information("Ana renk: {Accent}", AccentColor ?? "varsayilan");
    }

    /// <summary>
    /// Ozel ana rengi uygular. Renkler DynamicResource ile cozuldugu icin
    /// Application.Resources'a dogrudan yazilan bu anahtarlar, birlestirilmis
    /// tema sozlugundeki ayni anahtarlari EZER. null ise ezme kaldirilir.
    /// Not: Brush.Danger'a dokunulmaz — silme/hata her zaman kirmizi kalir.
    /// </summary>
    private void ApplyAccent()
    {
        var res = Application.Current.Resources;

        if (AccentColor is null)
        {
            foreach (var key in new[] { "Brush.Accent", "Brush.AccentHover", "Brush.AccentPressed", "Brush.AccentSubtle" })
                if (res.Contains(key)) res.Remove(key);
            return;
        }

        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(AccentColor); }
        catch { return; }

        res["Brush.Accent"] = Frozen(c);
        res["Brush.AccentHover"] = Frozen(Shift(c, IsDarkActive ? 0.14 : -0.10));
        res["Brush.AccentPressed"] = Frozen(Shift(c, IsDarkActive ? -0.10 : -0.20));
        // Cok hafif tint — hem acik hem koyu zeminde calisir.
        res["Brush.AccentSubtle"] = Frozen(Color.FromArgb(IsDarkActive ? (byte)0x2E : (byte)0x22, c.R, c.G, c.B));
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    /// <summary>Rengi acar (amount>0) veya koyulastirir (amount&lt;0).</summary>
    private static Color Shift(Color c, double amount)
    {
        double Adjust(byte channel) => amount >= 0
            ? channel + (255 - channel) * amount
            : channel * (1 + amount);

        return Color.FromRgb(
            (byte)Math.Clamp(Adjust(c.R), 0, 255),
            (byte)Math.Clamp(Adjust(c.G), 0, 255),
            (byte)Math.Clamp(Adjust(c.B), 0, 255));
    }

    /// <summary>Paleti degistirir; acik/koyu secimi korunur.</summary>
    public void SetPalette(string palette)
    {
        if (!Palettes.Contains(palette) || palette == Palette) return;
        Palette = palette;
        SaveSetting(PaletteKey, palette);
        Apply(Theme, persist: false);
    }

    /// <summary>Acik <-> koyu arasinda gecis yapar (System modundan cikar).</summary>
    public void Toggle() => Apply(IsDarkActive ? AppTheme.Light : AppTheme.Dark);

    private static bool IsWindowsUsingDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // AppsUseLightTheme: 0 = koyu, 1 = acik
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Windows tema ayari okunamadi, acik temaya duseluyor");
            return false;
        }
    }

    private AppTheme Load()
    {
        try
        {
            using var connection = _database.Open();
            var raw = connection.QuerySingleOrDefault<string>(
                "SELECT Value FROM Settings WHERE Key = @key", new { key = SettingKey });

            return Enum.TryParse<AppTheme>(raw, out var parsed) ? parsed : AppTheme.System;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Tema tercihi okunamadi");
            return AppTheme.System;
        }
    }

    private void Save(AppTheme theme)
    {
        try
        {
            using var connection = _database.Open();
            connection.Execute("""
                INSERT INTO Settings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """,
                new { key = SettingKey, value = theme.ToString() });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Tema tercihi kaydedilemedi");
        }
    }

    private string? LoadSetting(string key)
    {
        try
        {
            using var connection = _database.Open();
            return connection.QuerySingleOrDefault<string>(
                "SELECT Value FROM Settings WHERE Key = @key", new { key });
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ayar okunamadi: {Key}", key);
            return null;
        }
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            using var connection = _database.Open();
            connection.Execute("""
                INSERT INTO Settings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """, new { key, value });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Ayar kaydedilemedi: {Key}", key);
        }
    }

    private string? LoadAccent()
    {
        try
        {
            using var connection = _database.Open();
            var raw = connection.QuerySingleOrDefault<string>(
                "SELECT Value FROM Settings WHERE Key = @key", new { key = AccentKey });
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ana renk tercihi okunamadi");
            return null;
        }
    }

    private void SaveAccent(string? hex)
    {
        try
        {
            using var connection = _database.Open();
            connection.Execute("""
                INSERT INTO Settings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """,
                new { key = AccentKey, value = hex ?? string.Empty });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Ana renk tercihi kaydedilemedi");
        }
    }
}
