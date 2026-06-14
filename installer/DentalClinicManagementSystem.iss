#define MyAppName "Cruz-Nery Dental Clinic Management System"
#define MyAppVersion "1.2.3"
#define MyAppPublisher "Cruz-Nery Dental Clinic"
#define MyAppExeName "Dental Clinic Management System.exe"

[Setup]
AppId={{C8C3C5E4-7D6A-4E53-9E57-CRUZNERYDCMS}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Cruz-Nery Dental Clinic
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=CruzNeryDentalClinic-Setup-1.2.3
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\Icons\clinic.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent