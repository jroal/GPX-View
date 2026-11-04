; Inno Setup script for GPX View
; Build publish output first (single-file, self-contained win-x64), then compile this script.

#ifndef MyAppName
#define MyAppName "GPX View"
#endif
#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif
#ifndef MyAppPublisher
#define MyAppPublisher "GPX View"
#endif
#ifndef MyAppExeName
#define MyAppExeName "GPX View.exe"
#endif
#ifndef MyPublishDir
#define MyPublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{D2C84B9D-6B72-4E95-8B93-48E9DCE7D1B2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=GPX-View-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
