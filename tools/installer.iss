; Hostline RD - Inno Setup Script
; Version placeholder {#AppVersion} is replaced by Release.ps1
; Varsayilanlar Hostline RD. Release.ps1 her surum icin AppName/AppPublisher/AppGuid/UriScheme gecer.
; AppGuid ic surumde degismemeli: degisirse mevcut kurulumlar guncelleme yerine ikinci kopya kurar.

#ifndef AppName
  #define AppName "Hostline RD"
#endif
#ifndef AppPublisher
  #define AppPublisher "Hostline"
#endif
#ifndef AppGuid
  #define AppGuid "A74335E6-F072-4270-B846-B481BB4A8C87"
#endif
#ifndef UriScheme
  #define UriScheme "hostline"
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
WizardStyle=modern
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaustune kisayol olustur"; GroupDescription: "Ek islemler:"
Name: "startup"; Description: "Windows ile birlikte baslat"; GroupDescription: "Ek islemler:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} Kaldir"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

; URI protokolu (kullanici bazli - admin gerekmez)
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}"; ValueType: string; ValueName: ""; ValueData: "URL:{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\{#UriScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
; Etkilesimli kurulumda bitis ekraninda "baslat" secenegi
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} baslatilsin mi?"; Flags: nowait postinstall skipifsilent
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
  Result := True;
end;
