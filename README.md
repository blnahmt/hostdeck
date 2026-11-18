# RdpManager

Hostline RD: sekmeli RDP, VNC ve SSH bağlantı yöneticisi. Sadece Windows.


## Durum

**Faz 1 — tamamlandı**
- [x] Solution + proje iskeleti, DI, Serilog
- [x] `RdpHost` — ActiveX'i `WindowsFormsHost` içinde barındırma
- [x] 4 tuzağın çözümü: `WM_GETOBJECT`, `WM_MOUSEACTIVATE`, geciktirilmiş dispose, resize bloğu
- [x] Bağlantı durum makinesi + hata kodu çevirisi
- [x] Gerçek sunucuya bağlantı doğrulandı

**Faz 2 — tamamlandı**
- [x] SQLite + Dapper deposu (JSON blob şeması)
- [x] DPAPI parola koruması
- [x] Bağlantı listesi: grup başlıkları, arama, çift tıkla bağlan
- [x] Bağlantı editörü (tüm RDP ayar yüzeyi)
- [x] `.rdp` dosyası içe aktarma
- [x] 18 birim testi geçiyor

**Faz 3 — kod tamam, test bekliyor**
- [x] Dragablz sekmeleri, sekmeyi ayrı pencereye koparma
- [x] `SessionManager` — oturum/pencere/tam ekran kayıtları, "aynı sunucuyu iki kez açma"
- [x] Tam ekran: host re-parent ediliyor, oturum kopmuyor
- [x] Karışık DPI tespiti → `mstsc.exe` fallback (DPAPI ile şifreli parola dahil)
- [x] Sekme başlığında durum göstergesi, sağ tık menüsü, Ctrl+Alt+Del

**Cila turu — tamamlandı**
- [x] Kendi başlık çubuğu (`ChromeWindow` + `WindowChrome`), Windows chrome'u yok
- [x] Quicksand fontu gömülü, açık/koyu tema, ince kırmızı vurgu, gölge yok
- [x] Sunucu listesi ve oturumlar ayrı pencerelerde
- [x] Etiketler (çoklu), renk şeridi, kamera + pano yönlendirmesi
- [x] Sıralama modu + sürükleyerek elle sıralama
- [x] Etiket çipleriyle filtreleme (birden fazla seçilirse "herhangi biri")
- [x] Listede açık oturum göstergesi, bağlantıyı çoğaltma
- [x] Arka planda kopan oturum: sekme vurgusu + görev çubuğu yanıp sönmesi
- [x] Ağ kopmalarında otomatik yeniden bağlanma (5/10/20/30/30 sn, geri sayımlı)
- [x] Pencere yerleşimi hatırlanıyor; oturum penceresi ilk açılışta maximize
- [x] JSON dışa aktarma, .rdp + .json içe aktarma

**UX turu — tamamlandı**
- [x] Global kısayol + hızlı başlatıcı (Alt+M → Ctrl+Alt+M → Ctrl+Shift+M sırayla denenir)
- [x] Bağlantı listesinde sağ tık menüsü, ikonlu araç çubuğu, tutarlı ikon seti
- [x] Liste yoğunluğu (kompakt/rahat), tercih kaydediliyor
- [x] Dinamik sekme genişliği, kapat butonu yalnız aktif/hover sekmede
- [x] Bağlanırken belirsiz ilerleme çizgisi
- [x] "+" listesi son kullanıma göre sıralı

**Faz 4 — sıradaki:** Velopack ile otomatik güncelleme, tepsi ikonu, tek örnek

## Sürümler

Depodaki tek sürüm **Hostline RD** (`dotnet build` varsayılanı). Kod `-p:Edition=Internal` ile
şirket içi bir sürüm de üretebiliyor; o sürümün adı, ikonu ve adresleri depoya girmeyen
`src/RdpManager/Internal/` ve `tools/internal.ps1` dosyalarında durur.

## Ön koşullar

- **.NET 10 SDK** (10.0.302 ile doğrulandı)
- **AxImp.exe** — Visual Studio Installer → Değiştir → Tekil bileşenler → **.NET Framework 4.8 SDK**

## Kurulum

```bash
pwsh tools/Generate-Interop.ps1
```

`lib/AxMSTSCLib.dll` ve `lib/MSTSCLib.dll` üretilir. Bunlar repoya commit edilir — RDP ActiveX kontrolü için resmî NuGet paketi yoktur, 1Remote ve mRemoteNG de aynısını yapar.

```bash
dotnet build RdpManager.sln -c Debug
```

```bash
dotnet run --project src/RdpManager -c Debug
```

## Yapı

```
src/RdpManager/
  App.xaml(.cs)          DI, Serilog, global hata yakalama
  Models/                RdpConnection — tüm ayarlar, Faz 2'de JSON blob olarak saklanacak
  Rdp/
    RdpClientEx.cs       WndProc düzeltmeleri (kritik)
    RdpHost.xaml(.cs)    oturumun kendisi — asla yeniden yaratılmaz, re-parent edilir
    RdpDisconnectInfo.cs hata kodu → Türkçe mesaj
  Services/
    HostDisposeQueue.cs  geciktirilmiş dispose (çökme önleyici)
  ViewModels/ Views/
lib/                     üretilen interop DLL'leri
tools/Generate-Interop.ps1
```

## Bilinmesi gerekenler

- **`WM_MOUSEACTIVATE`'e dokunma.** `AxHost` bu mesajda OLE kontrolünü UI-active yapıyor; ele geçirilirse tıklamalar çalışır ama **sürükleme çalışmaz** (metin seçme, pencere taşıma). `base.WndProc`'a geçirmek bile yetmedi — mesajı hiç ele almamak gerekti.
- **Windows tuşu:** `KeyboardHookMode` varsayılanı 2'dir (yalnız tam ekranda uzak oturuma gider). Pencere modunda da gitmesi için 1 olmalı.
- **Airspace: WPF öğeleri `WindowsFormsHost`'un üstüne çizilemez.** RDP kontrolü bir HWND'dir; aynı pencerede onun üzerine konan her WPF öğesi görünmez olur. Bu yüzden (a) tam ekran araç çubuğu ayrı bir pencere (`FullScreenBar`), (b) `RdpHost`'un durum katmanı gösterilirken `WindowsFormsHost` gizleniyor.
- **`ChromeWindow` stili `Themes/Generic.xaml`'de olmak zorunda.** App.xaml'deki implicit style yalnızca tam tipe uygulanır, türetilmiş pencerelere miras kalmaz.
- **Sekme stili `ItemContainerStyle`'a açıkça atanmalı.** Dragablz kendi varsayılanını set ettiği için anahtarsız implicit style ezilir.
- **Tema sözlüğü sırası önemli.** WPF'te aynı anahtar için sonraki sözlük kazanır; `ThemeService` sözlüğü ekleme değil **değiştirme** yapar.


- **`RdpHost` asla yeniden yaratılmaz.** Tam ekran / sekme koparma işlemlerinde sadece re-parent edilir; yeniden yaratılırsa oturum kopar.
- **Dispose geciktirilmelidir.** `OnDisconnected` içindeyken ActiveX'i dispose etmek uygulamayı çökertir — `HostDisposeQueue` bunun içindir.
- **DPI ayarları `Connect()` öncesinde set edilmelidir**, sonrasında etkisizdir.
- **x64 zorunlu.** `mstscax.dll` interop'u AnyCPU ile 32-bit hostta patlar.
- Parola Faz 1'de sadece bellekte tutuluyor; diske yazılmıyor. DPAPI koruması Faz 2'de.

## Sonraki adım

Faz 2 — SQLite + Dapper deposu, DPAPI parola koruması, bağlantı listesi ve editör ekranı.
