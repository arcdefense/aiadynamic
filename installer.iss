; Inno Setup Script for Aia Dinamic
; Commercial-grade Windows Installer with Bundled Audio Equalizer Engine

#define MyAppName "Aia Dinamic"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "AiaStudio"
#define MyAppExeName "AiaDinamic.exe"

[Setup]
AppId={{5E97D4B2-9694-4F9A-A3AC-D0E993BF3B79}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=publish\installer
OutputBaseFilename=AiaDinamic-Setup-v1.0.0
SetupIconFile=app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Windows Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoOriginalFileName=AiaDinamic-Setup-v1.0.0.exe
VersionInfoCopyright=Copyright (C) 2026 AiaStudio. All rights reserved.
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Buat pintasan di Start Menu"; GroupDescription: "{cm:AdditionalIcons}"
Name: "runatstartup"; Description: "Jalankan otomatis saat Windows dinyalakan"; GroupDescription: "Pengaturan Startup:"
Name: "installeqengine"; Description: "Pasang Audio Equalizer Engine (Agar efek equalizer global di Spotify/YouTube/Game aktif)"; GroupDescription: "Studio Equalizer:"

[Files]
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "tools\EqualizerAPO-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "tools\EqualizerAPO-x64.exe"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AiaDinamic"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: runatstartup; Flags: uninsdeletevalue

[Run]
; Install Equalizer Engine if selected and not yet present
Filename: "{tmp}\EqualizerAPO-x64.exe"; Parameters: "/S"; StatusMsg: "Memasang Audio Equalizer Engine (Equalizer APO)..."; Tasks: installeqengine; Flags: waituntilterminated; Check: NeedsApoInstallation
; Launch Application
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsApoInstallation(): Boolean;
begin
  Result := not FileExists('C:\Program Files\EqualizerAPO\config\config.txt') and
            not FileExists('C:\Program Files (x86)\EqualizerAPO\config\config.txt') and
            not DirExists('C:\Program Files\EqualizerAPO');
end;
