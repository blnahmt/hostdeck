; Hostdeck - Inno Setup Script
; Version placeholder {#AppVersion} is replaced by Release.ps1
; Varsayilanlar Hostdeck. Release.ps1 her surum icin AppName/AppPublisher/AppGuid/UriScheme gecer.
; AppGuid ic surumde degismemeli: degisirse mevcut kurulumlar guncelleme yerine ikinci kopya kurar.

#ifndef AppName
  #define AppName "Hostdeck"
#endif
#ifndef AppPublisher
  #define AppPublisher "Ahmet Bilen"
#endif
#ifndef AppGuid
  #define AppGuid "A74335E6-F072-4270-B846-B481BB4A8C87"
#endif
#ifndef UriScheme
  #define UriScheme "hostdeck"
#endif
#define AppExeName AppName + ".exe"

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputFilename}
Compression=lzma2/max
SolidCompression=yes
; Windows acik/koyu ayarina uyar.
WizardStyle=modern dynamic
ShowLanguageDialog=auto
#ifdef HostdeckBrand
; Hostdeck markasi (gorseller: tools\Make-InstallerImages.ps1). Release.ps1 yalniz Hostdeck icin verir;
; ic surum varsayilan gorunumde kalir.
WizardImageFile=installer\wizard-light-100.png,installer\wizard-light-150.png,installer\wizard-light-200.png
WizardImageFileDynamicDark=installer\wizard-dark-100.png,installer\wizard-dark-150.png,installer\wizard-dark-200.png
WizardSmallImageFile=installer\small-100.png,installer\small-150.png,installer\small-200.png
WizardSmallImageFileDynamicDark=installer\small-100.png,installer\small-150.png,installer\small-200.png
WizardImageBackColor=#FFFFFF
WizardImageBackColorDynamicDark=#0A0A0A
WizardBackColor=#FFFFFF
WizardBackColorDynamicDark=#0A0A0A
; Buyuk marka gorseli karsilama sayfasinda gorunur (Inno 6 varsayilani bu sayfayi atlar).
DisableWelcomePage=no
; 1.3.0 "Hostline RD" adiyla kuruldu (ayni AppGuid). Klasor ve baslat menusu grubu yeni adi alsin.
UsePreviousAppDir=no
UsePreviousGroup=no
#endif
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Languages]
; Windows diline gore otomatik secilir (ShowLanguageDialog=auto).
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.AdditionalOptions=Additional options:
turkish.AdditionalOptions=Ek seçenekler:
english.StartWithWindows=Start with Windows
turkish.StartWithWindows=Windows ile birlikte başlat

[Tasks]
; Gorev adlari degismemeli: mevcut kurulumlarin secimleri bu adlarla hatirlaniyor.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalOptions}"
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:AdditionalOptions}"

#ifdef HostdeckBrand
[InstallDelete]
; Eski ad (Hostline RD, 1.3.0 ve oncesi): eski klasor, kisayollar.
Type: filesandordirs; Name: "{autopf}\Hostline RD"
Type: filesandordirs; Name: "{autoprograms}\Hostline RD"
Type: files; Name: "{autodesktop}\Hostline RD.lnk"
#endif

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
#ifdef HostdeckBrand
; Eski ad: Windows ile baslat kaydi ve hostline:// protokolu.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Hostline RD"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Classes\hostline"; ValueType: none; Flags: deletekey
#endif
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

; URI protokolu (kullanici bazli - admin gerekmez)
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}"; ValueType: string; ValueName: ""; ValueData: "URL:{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
; Etkilesimli kurulumda bitis ekraninda "baslat" secenegi
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Sessiz kurulumda (guncelleme) uygulamayi otomatik yeniden baslat
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/F /IM ""{#AppExeName}"""; Flags: runhidden

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Kurulumdan once calisan uygulamayi kapat
  Exec('taskkill', '/F /IM "{#AppExeName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
#ifdef HostdeckBrand
  // Eski adla calisan surum (guncelleme Hostline RD 1.3.0 icinden baslatildiysa)
  Exec('taskkill', '/F /IM "Hostline RD.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
#endif
  Result := True;
end;
