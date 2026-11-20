namespace RdpManager;

/// <summary>
/// Iki surum ayni koddan derlenir: dotnet publish -p:Edition=Internal|Public (varsayilan Internal).
/// Internal degerleri eski surumle birebir ayni kalmali; yoksa mevcut kurulumlar guncellenemez.
/// </summary>
public static class AppEdition
{
#if PUBLIC_EDITION
    public const string Name = "Hostdeck";
    public const string Id = "Hostdeck";              // mutex + AppData klasoru
    // Eski adla (Hostline RD, marka cakismasi nedeniyle degisti) kurulmus verinin klasoru; ilk acilista tasinir.
    public static readonly string[] LegacyIds = ["HostlineRD"];
    public const string UriScheme = "hostdeck";
    // GitHub her zaman en son (on surum olmayan) release'in dosyasina yonlendirir. Depo herkese acik olmali.
    public const string UpdateUrl = "https://github.com/blnahmt/hostdeck/releases/latest/download/latest.json";
    public static bool HasServerStatus => false;      // sirket ici doluluk API'si yalniz ic surumde
    public const string DefaultPalette = "Hostdeck";
    public static bool IsPublic => true;
    public const bool FollowsSystemLanguage = true;    // ilk acilista Windows dili (TR degilse EN)
#else
    // Degerler git'e girmeyen Internal/InternalEdition.cs'te.
    public const string Name = InternalEdition.Name;
    public const string Id = InternalEdition.Id;
    public const string UriScheme = InternalEdition.UriScheme;
    public const string UpdateUrl = InternalEdition.UpdateUrl;
    public static readonly string[] LegacyIds = ["RdpManager"];
    public static bool HasServerStatus => true;
    public const string DefaultPalette = "Klasik";
    public static bool IsPublic => false;
    public const bool FollowsSystemLanguage = false;   // ic surum Turkce baslar
#endif
}
