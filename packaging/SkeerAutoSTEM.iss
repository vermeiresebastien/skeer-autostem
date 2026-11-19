; Skeer autoSTEM installer - Inno Setup 6
;
;   ISCC.exe /DAppVersion=x.y.z packaging\SkeerAutoSTEM.iss
;   (x.y.z = <Version> in src\StemForge.App\StemForge.App.csproj - build.ps1 passes it there)
;
; Relative paths resolve against this script's own folder (packaging\), so ..\ reaches the
; repository root. Run .\build.ps1 first: this script packages the binaries it publishes and
; fails if they are missing.

#define AppName "Skeer autoSTEM"

; The version has one source, the csproj above. Nothing here defaults it: a defaulted literal
; is a second copy that drifts, which is exactly what this file used to carry.
#ifndef AppVersion
#error "AppVersion is not defined: build through build.ps1, or pass /DAppVersion=x.y.z taken from the csproj <Version>"
#endif

[Setup]
; AppId is this product's permanent identity: never change it once shipped, or a new build
; installs side by side instead of replacing the old one.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Skeer
AppPublisherURL=https://github.com/vermeiresebastien/skeer-autostem
AppSupportURL=https://github.com/vermeiresebastien/skeer-autostem/issues
AppUpdatesURL=https://github.com/vermeiresebastien/skeer-autostem/releases
DefaultDirName={autopf}\Skeer autoSTEM
DefaultGroupName=Skeer autoSTEM
AllowNoIcons=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\SkeerAutoSTEM.exe
SetupIconFile=..\src\StemForge.App\Brand\skeer.ico
OutputDir=..\artifacts\installers
OutputBaseFilename=Skeer_autoSTEM_Setup_v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install: the app needs no elevation to run, and the audio associations below are
; written to HKCU, which only behaves predictably when the install runs as the same user.
PrivilegesRequired=lowest
; Required whenever the [Registry] section touches file classes: without it neither the
; install nor the uninstall tells the shell to reload its association table.
ChangesAssociations=yes
CloseApplications=yes

[Files]
; The desktop app and the headless companion: both self-contained single files, so Ableton
; aside, no runtime needs to be installed.
Source: "..\artifacts\SkeerAutoSTEM\SkeerAutoSTEM.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\autostem-cli\autostem.exe"; DestDir: "{app}"; Flags: ignoreversion
; A self-contained publish redistributes the .NET runtime, and Microsoft's licence requires the
; notices to travel with it. build.ps1 copies both texts out of the runtime packs it published
; from, so they sit next to each exe in artifacts\. One copy per install is enough: the two exes
; share {app}, and they are the same files whoever installs them.
Source: "..\artifacts\SkeerAutoSTEM\ThirdPartyNotices.dotnet.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\SkeerAutoSTEM\LICENSE.dotnet-runtime.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\SkeerAutoSTEM.exe"; WorkingDir: "{app}"
Name: "{group}\Third-party notices (.NET runtime)"; Filename: "{app}\ThirdPartyNotices.dotnet.txt"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\SkeerAutoSTEM.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Registry]
; Opening an audio file from Explorer queues it in the app. Registered under HKCU, not HKCR,
; so the install never replaces the machine-wide handler for every .wav on this desk, and
; uninsdeletekey hands the format back to the previous player on uninstall.
; The default handler is taken over for the working-file formats only, because those are the
; files this desk is produced in. MP3 is offered in "Open with" and keeps its player.
Root: HKCU; Subkey: "Software\Classes\SkeerAutoSTEM.Audio"; ValueType: string; ValueName: ""; ValueData: "Skeer autoSTEM batch input"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SkeerAutoSTEM.Audio\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\SkeerAutoSTEM.exe"" ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SkeerAutoSTEM.Audio\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\SkeerAutoSTEM.exe"",0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.wav"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.Audio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.aif"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.Audio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.aiff"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.Audio"; Flags: uninsdeletekey
; Windows protects a hashed UserChoice from installer-written defaults (on this desk .wav is
; claimed by a packaged Media Player), so also offer the ProgID in "Open with": picking Skeer
; autoSTEM there once and choosing Always is the only way an installer can win that type.
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "SkeerAutoSTEM.Audio"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.aif\OpenWithProgids"; ValueType: string; ValueName: "SkeerAutoSTEM.Audio"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueType: string; ValueName: "SkeerAutoSTEM.Audio"; ValueData: ""; Flags: uninsdeletevalue
; Offered, not defaulted: an MP3 opened from Explorer queues it, and Live reads the file in
; place. Other compressed formats are absent on purpose - the app still cannot read them.
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "SkeerAutoSTEM.Audio"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\SkeerAutoSTEM.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
