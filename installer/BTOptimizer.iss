; ============================================================================
;  Installeur BT Optimizer (Inno Setup)
;  Compile avec Inno Setup 6+ :  double-clic sur ce fichier dans Inno Setup,
;  ou en ligne de commande :  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" BTOptimizer.iss
;
;  Prérequis exécution client : .NET Desktop Runtime 10.x (x64).
;  AVANT de compiler : lance Build.bat pour produire le dossier ..\dist
; ============================================================================

#define AppName "BT Optimizer"
#define AppVersion "6.1.0"
#define AppPublisher "VOTRE NOM / SOCIÉTÉ"
#define AppURL "https://votresite.example"
#define AppExe "BTOptimizer.exe"

[Setup]
AppId={{9F1C7A20-BT01-4E5A-9C3D-BTOPTIMIZER0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=Output
OutputBaseFilename=BTOptimizer-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=LICENSE.txt
DisableProgramGroupPage=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Components]
Name: "app";   Description: "Application BT Optimizer"; Types: full compact custom; Flags: fixed
Name: "tools"; Description: "Outils optionnels (nvidiaProfileInspector pour le profil NVIDIA)"; Types: full

[Files]
; Le binaire .NET 10 (produit par Build.bat dans ..\dist)
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app
; Outils optionnels — vérifiez vos droits de redistribution avant diffusion.
Source: "..\tools\npi\*"; DestDir: "{app}\tools\npi"; Flags: ignoreversion recursesubdirs; Components: tools
Source: "..\tools\input-lag-reapply.nip"; DestDir: "{app}\tools"; Flags: ignoreversion skipifsourcedoesntexist; Components: tools

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
// Avertit si le .NET Desktop Runtime 10 semble absent (l'app en a besoin).
function InitializeSetup(): Boolean;
var
  Base: string;
begin
  Result := True;
  Base := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(Base) then
  begin
    if MsgBox('Le .NET Desktop Runtime 10 (x64) ne semble pas installé.' + #13#10 +
              'BT Optimizer en a besoin pour fonctionner.' + #13#10#13#10 +
              'Télécharge-le sur https://dotnet.microsoft.com/download/dotnet/10.0' + #13#10 +
              '(section « .NET Desktop Runtime »).' + #13#10#13#10 +
              'Continuer l''installation quand même ?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
