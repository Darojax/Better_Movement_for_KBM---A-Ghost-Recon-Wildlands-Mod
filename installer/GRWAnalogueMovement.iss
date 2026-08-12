#define AppName "Better Movement for KBM"
#define AppVersion "0.1.0-beta"
#define AppPublisher "GRW Analogue Movement Mod contributors"
#define SourceDir "..\artifacts\portable\GRW-Analogue-Movement-Mod"

[Setup]
AppId={{D2444EF3-5BC0-4C15-B260-5B97023C0BA7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\GRW Analogue Movement Mod
DefaultGroupName={#AppName}
LicenseFile=..\release\DISCLAIMER.txt
OutputDir=..\artifacts\installer
OutputBaseFilename=Better-Movement-for-KBM-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\Better Movement for KBM - GRW.exe
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Better Movement for KBM"; Filename: "{app}\Better Movement for KBM - GRW.exe"
Name: "{group}\Read Me"; Filename: "{app}\README.md"
Name: "{autodesktop}\Better Movement for KBM"; Filename: "{app}\Better Movement for KBM - GRW.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Better Movement for KBM - GRW.exe"; Description: "Open Better Movement for KBM"; Flags: postinstall nowait skipifsilent
