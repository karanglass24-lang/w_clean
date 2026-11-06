; Inno Setup script.
; Build: ISCC /DAppVersion=0.1.0 /DSourceDir=..\artifacts\publish installer\WClean.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{B7C4E1A2-5D3F-4A8B-9E61-2F0C7D9A4B35}
AppName=W Clean
AppVersion={#AppVersion}
AppPublisher=W Clean
DefaultDirName={autopf}\W Clean
DefaultGroupName=W Clean
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\WClean.exe
UninstallDisplayName=W Clean
CloseApplications=yes
SetupIconFile=..\assets\WClean.ico
OutputBaseFilename=WClean-{#AppVersion}-win-x64-setup
OutputDir=..\artifacts
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\WClean.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\W Clean"; Filename: "{app}\WClean.exe"
Name: "{autodesktop}\W Clean"; Filename: "{app}\WClean.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WClean.exe"; Description: "{cm:LaunchProgram,W Clean}"; Flags: nowait postinstall skipifsilent shellexec
