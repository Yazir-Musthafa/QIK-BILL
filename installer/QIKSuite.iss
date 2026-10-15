; QIK Suite installer (Inno Setup 6).
; Built by scripts/release.ps1:  ISCC.exe /DAppVersion=1.2.3 /DSourceDir=<publish folder> /DOutputDir=<folder> installer\QIKSuite.iss
;
; - Installs for the current user only (no administrator rights) into %LOCALAPPDATA%\Programs\QIK Suite.
; - Start menu shortcut, optional desktop shortcut, uninstaller in Apps & features.
; - Company data lives in %LOCALAPPDATA%\QIKSuite and is never deleted by uninstall.
; - Upgrades install over the previous version (same AppId). Before replacing the program, a copy of the database is
;   kept beside it; the new version then migrates the database on first start.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{6C1E2F7A-4B3D-4E59-9A8C-2F1D7B6E5A40}
AppName=QIK Suite
AppVersion={#AppVersion}
AppVerName=QIK Suite {#AppVersion}
AppPublisher=QIK
DefaultDirName={localappdata}\Programs\QIK Suite
DefaultGroupName=QIK Suite
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=QIKSuite-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=QIK Suite
UninstallDisplayIcon={app}\QIKSuite.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
LicenseFile={#SourceDir}\licenses\THIRD-PARTY-NOTICES.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\QIK Suite"; Filename: "{app}\QIKSuite.exe"
Name: "{group}\Uninstall QIK Suite"; Filename: "{uninstallexe}"
Name: "{autodesktop}\QIK Suite"; Filename: "{app}\QIKSuite.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\QIKSuite.exe"; Description: "Start QIK Suite"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  DataFolder = '{localappdata}\QIKSuite';

{ Before an upgrade replaces the program, keep a copy of the database (and its write-ahead log) beside it. }
procedure KeepDatabaseCopy();
var
  Folder, Stamp, Target: String;
begin
  Folder := ExpandConstant(DataFolder);
  if not FileExists(Folder + '\qiksuite.db') then exit;
  Stamp := GetDateTimeString('yyyymmdd-hhnnss', #0, #0);
  Target := Folder + '\qiksuite.db.before-upgrade-' + Stamp;
  if FileCopy(Folder + '\qiksuite.db', Target, False) then
  begin
    if FileExists(Folder + '\qiksuite.db-wal') then
      FileCopy(Folder + '\qiksuite.db-wal', Target + '-wal', False);
    Log('Database copied to ' + Target);
  end
  else
    Log('Could not copy the database before the upgrade.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then KeepDatabaseCopy();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
    MsgBox('QIK Suite was removed. Your company data and backups were kept in ' + ExpandConstant(DataFolder) +
      '. Delete that folder yourself if you no longer need them.', mbInformation, MB_OK);
end;
