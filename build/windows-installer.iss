; Compile with build/installer.ps1. The payload is the verified self-contained release.
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#ifndef InstallerOutput
  #define InstallerOutput "..\artifacts\installer"
#endif

[Setup]
AppId={{C67A3A80-84E9-42DC-973B-09850DAD27E2}
AppName=Octadock
AppVersion={#ReleaseVersion}
AppVerName=Octadock {#ReleaseVersion}
AppPublisher=Prime Ashref
AppPublisherURL=https://github.com/achrefbs
AppSupportURL=https://octadock.com/
AppUpdatesURL=https://octadock.com/download.html
DefaultDirName={localappdata}\Programs\Octadock
DefaultGroupName=Octadock
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#InstallerOutput}
OutputBaseFilename=Octadock-{#ReleaseVersion}-Setup
SetupIconFile=..\src\Octadock.App\Resources\Icons\octadock.ico
UninstallDisplayIcon={app}\Octadock.exe
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName=Octadock
VersionInfoVersion=0.3.0.2

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\publish\octadock\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\release-manifest.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Octadock"; Filename: "{app}\Octadock.exe"
Name: "{autodesktop}\Octadock"; Filename: "{app}\Octadock.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Octadock.exe"; Description: "Open Octadock"; Flags: nowait postinstall skipifsilent

[Code]
// Remove only integration values that still point at this installation.
// A separate portable copy and all captures/settings in LocalAppData\Octadock survive.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Octadock', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\Octadock.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Octadock');
    if RegQueryStringValue(HKCU, 'Software\Classes\octadock\shell\open\command', '', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\Octadock.exe') + '" "%1"') = 0 then
        RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\octadock');
  end;
end;
