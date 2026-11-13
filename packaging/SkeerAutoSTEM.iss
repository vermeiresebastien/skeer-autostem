; Skeer autoSTEM Installer
; Inno Setup script for Windows 11

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName=Skeer autoSTEM
AppVersion=1.1.0
AppPublisher=Skeer
AppPublisherURL=https://github.com/sebastienvermeire/skeer-autostem
AppSupportURL=https://github.com/sebastienvermeire/skeer-autostem/issues
AppUpdatesURL=https://github.com/sebastienvermeire/skeer-autostem/releases
DefaultDirName={autopf}\Skeer autoSTEM
DefaultGroupName=Skeer autoSTEM
AllowNoIcons=yes
LicenseFile=
OutputDir=artifacts\installers
OutputBaseFilename=Skeer_autoSTEM_Setup_v{#AppVersion}
SetupIconFile=
Compression=lzma2
CompressionLevel=ultra
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
; The main executable (self-contained .NET 10 app)
Source: "artifacts\SkeerAutoSTEM\SkeerAutoSTEM.exe"; DestDir: "{app}"; Flags: ignoreversion
; The CLI companion tool
Source: "artifacts\autostem-cli\autostem.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Desktop shortcut
Name: "{commondesktop}\Skeer autoSTEM"; Filename: "{app}\SkeerAutoSTEM.exe"; WorkingDir: "{app}"
; Start menu shortcut
Name: "{group}\Skeer autoSTEM"; Filename: "{app}\SkeerAutoSTEM.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Skeer autoSTEM"; Filename: "{uninstallexe}"; IconFilename: "{app}\SkeerAutoSTEM.exe"

[Run]
; Launch the app after installation
Filename: "{app}\SkeerAutoSTEM.exe"; Description: "Launch Skeer autoSTEM"; Flags: nowait postinstall skipifsilent

[Registry]
; Register the file association for .wav, .aif, .aiff, .mp3, .m4a, .flac, .ogg
Root: HKCR; Subkey: ".wav"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.WAVFile"; Flags: noenvreplace
Root: HKCR\SkeerAutoSTEM.WAVFile; ValueType: string; ValueName: ""; ValueData: "Skeer autoSTEM WAV File"; Flags: noenvreplace
Root: HKCR\SkeerAutoSTEM.WAVFile\shell\open\command; ValueType: string; ValueName: ""; ValueData: """{app}\SkeerAutoSTEM.exe"" ""%1""""; Flags: noenvreplace

; Register file association for other audio formats
Root: HKCR; Subkey: ".aif"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.AudioFile"; Flags: noenvreplace
Root: HKCR; Subkey: ".aiff"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.AudioFile"; Flags: noenvreplace
Root: HKCR; Subkey: ".mp3"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.AudioFile"; Flags: noenvreplace
Root: HKCR; Subkey: ".m4a"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.AudioFile"; Flags: noenvreplace
Root: HKCR; Subkey: ".flac"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.AudioFile"; Flags: noenvreplace
Root: HKCR; Subkey: ".ogg"; ValueType: string; ValueName: ""; ValueData: "SkeerAutoSTEM.AudioFile"; Flags: noenvreplace

Root: HKCR\SkeerAutoSTEM.AudioFile; ValueType: string; ValueName: ""; ValueData: "Skeer autoSTEM Audio File"; Flags: noenvreplace
Root: HKCR\SkeerAutoSTEM.AudioFile\shell\open\command; ValueType: string; ValueName: ""; ValueData: """{app}\SkeerAutoSTEM.exe"" ""%1""""; Flags: noenvreplace

; Register the app in "Open with" menu
Root: HKCR\*\shell\Skeer autoSTEM; ValueType: string; ValueName: ""; ValueData: "Open with Skeer autoSTEM"; Flags: noenvreplace
Root: HKCR\*\shell\Skeer autoSTEM\command; ValueType: string; ValueName: ""; ValueData: """{app}\SkeerAutoSTEM.exe"" ""%1""""; Flags: noenvreplace

[UninstallDelete]
; Clean up AppData on uninstall
Type: files; Name: "{appdata}\SkeerAutoSTEM\*"
Type: files; Name: "{appdata}\SkeerAutoSTEM\*"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  // Check if Ableton Live is installed (optional, for informational purposes)
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Ableton Live 12 Suite', 'InstallLocation', @ResultCode) then
  begin
    // Ableton Live is installed, but we don't fail the installation
    // The user can still use the simulated engine
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Open the app data folder for the user
    ShellExec('Open', 'explorer.exe', '{appdata}\SkeerAutoSTEM', '', SW_SHOW, ewWaitUntilTerminated, erIgnoreReturn);
  end;
end;
