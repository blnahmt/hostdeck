# Geliştirici notları

Hostdeck'in iç yapısı ve dikkat edilmesi gereken tuzaklar. Kullanıcı belgeleri için [README](../README.md).

## Sürümler

Depodaki tek sürüm **Hostdeck** (`dotnet build` varsayılanı). Kod `-p:Edition=Internal` ile
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

```text
src/RdpManager/
  App.xaml(.cs)        açılış, tek kopya kilidi, dil, DI, Serilog, global hata yakalama
  AppEdition.cs        sürüme göre ad/kimlik/adresler (Hostdeck / iç sürüm)
  Rdp/
    ISessionHost.cs    sekmenin gördüğü ortak yüzey (RDP/VNC/SSH)
    RdpHost.xaml(.cs)  RDP oturumu (ActiveX) — asla yeniden yaratılmaz, re-parent edilir
    RdpClientEx.cs     WndProc düzeltmeleri (kritik)
    VncHost.cs         VNC oturumu (MarcusW.VncClient) + VncFramebuffer, VncKeyMap
    SshHost.cs         SSH oturumu (SSH.NET + WebView2/xterm.js) + SshKnownHosts
  Services/            SessionManager, ThemeService, TrayIconService, UpdateService,
                       ReachabilityService, HostDisposeQueue (geciktirilmiş dispose) ...
  Data/                SQLite + Dapper (bağlantılar JSON sütununda), .rdp ve JSON aktarımı
  Security/            DPAPI (CredentialProtector), AES-256-GCM dışa aktarma (PassphraseCipher)
  Localization/        Loc + en/*.tsv sözlükleri (Türkçe metin anahtardır)
  Terminal/            SSH sekmesinin xterm.js sayfası
  Internal/            (git'e girmez) iç sürümün ad/ikon/adresleri
lib/                   üretilen RDP interop DLL'leri
tools/                 Release.ps1, installer.iss, Generate-Interop.ps1, Make-HostdeckIcon.ps1
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
