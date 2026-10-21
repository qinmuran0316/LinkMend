#ifndef AppVersion
  #error Build with scripts/build-installer.ps1.
#endif

[Setup]
AppId=qinmuran0316.LinkMend
AppName=LinkMend
AppVersion={#AppVersion}
AppPublisher=qinmuran0316
AppPublisherURL=https://github.com/qinmuran0316/LinkMend
AppSupportURL=https://github.com/qinmuran0316/LinkMend/issues
AppUpdatesURL=https://github.com/qinmuran0316/LinkMend
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=LinkMend Setup
DefaultDirName={localappdata}\Programs\LinkMend
DefaultGroupName=LinkMend
DisableDirPage=no
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
LicenseFile={#RepoRoot}\LICENSE
SetupIconFile={#RepoRoot}\src\LinkMend.App\Assets\LinkMend.ico
UninstallDisplayIcon={app}\LinkMend.exe
UninstallDisplayName=LinkMend {#AppVersion}
AppMutex=Local\LinkMend.Running
SetupMutex=Local\LinkMend.Setup
CloseApplications=no
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=LinkMend-v{#AppVersion}-win-x64-Setup
Compression=lzma2/fast
SolidCompression=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#ChineseMessages}"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\LinkMend.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LinkMend"; Filename: "{app}\LinkMend.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,LinkMend}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LinkMend"; Filename: "{app}\LinkMend.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LinkMend.exe"; Description: "{cm:LaunchProgram,LinkMend}"; Flags: nowait postinstall skipifsilent
