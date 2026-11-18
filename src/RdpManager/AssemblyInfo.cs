using System.Windows;

// ChromeWindow'un varsayilan stilinin Themes/Generic.xaml'den cozulebilmesi icin
// sart. Bu oznitelik olmadan DefaultStyleKey tema sozlugunu bulamaz.
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            // tema-ozel sozluk yok
    ResourceDictionaryLocation.SourceAssembly)] // genel sozluk bu assembly'de
