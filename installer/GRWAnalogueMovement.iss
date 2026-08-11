#define AppName "GRW Analogue Movement Mod"
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
OutputBaseFilename=GRW-Analogue-Movement-Mod-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\GRWAnalogueMovement.exe
WizardStyle=modern

[Tasks]
Name: "standardfirewall"; Description: "Install recommended GRW-only outbound firewall rules"; GroupDescription: "Offline isolation:"; Flags: checkedonce exclusive
Name: "strictfirewall"; Description: "Install strict GRW + Ubisoft Connect firewall rules (may prevent login and updates)"; GroupDescription: "Offline isolation:"; Flags: unchecked exclusive
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GRW Analogue Movement"; Filename: "{app}\GRWAnalogueMovement.exe"
Name: "{group}\Safety Setup"; Filename: "{app}\GRWMovementSafety.exe"
Name: "{group}\Read Me"; Filename: "{app}\README.md"
Name: "{autodesktop}\GRW Analogue Movement"; Filename: "{app}\GRWAnalogueMovement.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GRWMovementSafety.exe"; Parameters: "--install-standard"; Description: "Install recommended GRW-only isolation"; Flags: runhidden waituntilterminated; Tasks: standardfirewall
Filename: "{app}\GRWMovementSafety.exe"; Parameters: "--install-strict"; Description: "Install strict isolation"; Flags: runhidden waituntilterminated; Tasks: strictfirewall
Filename: "{app}\GRWMovementSafety.exe"; Parameters: "--status"; Description: "Show safety status"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\GRWMovementSafety.exe"; Parameters: "--remove-firewall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveGRWMovementFirewallRules"
