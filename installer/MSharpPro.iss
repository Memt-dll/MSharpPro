; M#Pro kurulum betiği (Inno Setup 6)
; Derlemek için:  build-installer.ps1  (veya GitHub Actions -> Release iş akışı)

#define AppName      "M#Pro"
#define AppExe       "MSharpPro.exe"
#define AppPublisher "Memt-dll"
#define AppUrl       "https://github.com/Memt-dll/MSharpPro"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{78FBAE63-50F3-4E5F-A54E-425928ACF0E2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
; Yönetici izni gerektirmeden kullanıcı klasörüne kurulur.
; "Tüm kullanıcılar için kur" seçeneği kapalı ve önceki kurulumun yetki modu yeniden kullanılmaz:
; böylece otomatik güncelleme hiçbir zaman Windows'un yönetici izni (UAC) penceresini açmaz.
DefaultDirName={localappdata}\Programs\MSharpPro
PrivilegesRequired=lowest
UsePreviousPrivileges=no
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MSharpPro-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Kurulum/güncelleme sırasında açık olan M#Pro'yu kapat
CloseApplications=force
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.StartupTask=Bilgisayar açılınca M#Pro'yu başlat
english.StartupTask=Start M#Pro when Windows starts
turkish.OtherTasks=Diğer seçenekler:
english.OtherTasks=Other options:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:OtherTasks}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Uygulamanın kendi "Bilgisayar açılınca başlat" ayarıyla aynı kayıt.
; Otomatik (sessiz) güncellemede dokunulmaz: kullanıcı uygulamadan kapattıysa geri açılmasın.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "M#Pro"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup; Check: not WizardSilent
; Uygulama bu değerle kurulu kopya olduğunu anlar (otomatik güncelleme yalnızca kurulu kopyada çalışır)
Root: HKCU; Subkey: "Software\MSharpPro"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Otomatik güncelleme sessiz kurulumla yapılır; bitince uygulamayı yeniden aç
; runasoriginaluser: kurulum bir şekilde yönetici olarak çalıştıysa bile uygulama normal kullanıcı olarak açılır
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "KillMSharpPro"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\MSharpPro"

[Code]
// Kullanıcı başlangıç ayarını uygulamadan açtıysa kaldırırken onu da temizle
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'M#Pro');
end;
