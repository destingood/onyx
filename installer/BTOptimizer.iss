; ============================================================================
;  Installateur ONYX (Inno Setup 6.3+)
;
;  Compilation (le plus simple) : double-clic sur ..\Build-Installer.bat
;  Manuel :  1) publie l'app :  dotnet publish -c Release -o dist
;            2) compile ce script : "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" BTOptimizer.iss
;  Résultat :  installer\Output\BTOptimizer-Setup-<version>.exe
;
;  Deux modes de diffusion, détectés AUTOMATIQUEMENT selon le contenu de ..\dist :
;   • Dépendant du runtime (Build-Installer.bat) : ~16 Mo, exige .NET Desktop 10 (x64)
;     côté client -> le script le vérifie et propose la page de téléchargement s'il manque.
;   • AUTONOME / self-contained (Build-Standalone.bat) : ~120 Mo, runtime embarqué,
;     s'installe et tourne SANS aucun prérequis. Idéal pour une diffusion grand public.
; ============================================================================

#define AppName "ONYX"
#define AppExe "BTOptimizer.exe"
; La version est lue automatiquement depuis le binaire publié (évite toute dérive).
#ifexist "..\dist\BTOptimizer.exe"
  #define AppVersion GetVersionNumbersString("..\dist\BTOptimizer.exe")
#else
  #define AppVersion "7.9.0.0"
#endif
#define AppPublisher "ONYX"
; Boutique officielle (cohérent avec l'app et la landing). À remplacer par l'URL de la
; landing hébergée dès qu'elle existe.
#define AppURL "https://fluide.gumroad.com"

; Détection AUTOMATIQUE d'une publication AUTONOME (self-contained), DEUX formes possibles :
;  · éclatée      : coreclr.dll est posé à côté de l'exe ;
;  · FICHIER UNIQUE : tout est DANS l'exe (aucun coreclr.dll sur le disque) -> on le reconnaît
;    à la taille du binaire (> 40 Mo). Sans ce test, une publication single-file était prise
;    pour du « dépendant du runtime » et l'installateur réclamait .NET à tort au client.
#ifexist "..\dist\coreclr.dll"
  #define SelfContained
#endif
#ifexist "..\dist\BTOptimizer.exe"
  #if !defined(SelfContained) && FileSize("..\dist\BTOptimizer.exe") > 40000000
    #define SelfContained
    #define SingleFile
  #endif
#endif

[Setup]
AppId={{2B539D2F-3B49-466B-B095-FEB9A1123E65}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
OutputDir=Output
OutputBaseFilename=ONYX-Setup-{#AppVersion}
SetupIconFile=..\src\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=LICENSE.txt
DisableProgramGroupPage=yes
; Empêche l'installation/désinstallation pendant que l'app tourne (mutex du Program.cs).
AppMutex=BTOptimizer_SingleInstance

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"
; Cerveau IA local (Ollama, gratuit, 100 % hors-ligne). Coché par défaut : l'app installe
; toute seule, au 1er lancement, le modèle ADAPTÉ à la machine (voir LocalBrain.Bootstrap).
; Cette case ne fait qu'écrire le consentement (voir [Code]) — aucun téléchargement pendant
; le setup, pour ne pas l'alourdir : l'app télécharge le modèle en fond, avec progression.
Name: "installia"; Description: "Installer le cerveau IA local (gratuit, ~2 Go) — le Copilote répond alors à TOUT, 100 % sur ce PC"; GroupDescription: "Assistant IA :"

[Components]
Name: "app";    Description: "Application ONYX";                                        Types: full compact custom; Flags: fixed
Name: "nvidia"; Description: "Profil pilote NVIDIA faible latence (nvidiaProfileInspector)";     Types: full

[Files]
; Binaires .NET 10 (produits par « dotnet publish -o dist »).
; On exclut les fichiers d'état générés à l'exécution et les symboles de débogage.
; ANTI-FUITE : tout ce qui n'est pas le binaire est exclu explicitement — donnees de l'utilisateur
; developpeur (memoire du Copilote, faits appris, journal, jeton de mise a jour), symboles de
; debogage et sources. « bt-*.md » manquait : bt-appris.md (conversations apprises) partait chez
; TOUS les utilisateurs. Le probe BT_RELEASE verifie ce dossier avant chaque publication.
#ifdef SingleFile
; ---- FICHIER UNIQUE : on livre EXACTEMENT l'exécutable, et rien d'autre. -----------------
; Liste EXPLICITE (et non « dist\* ») : le dossier de publication contient aussi les données
; de test du développeur et parfois des restes d'une publication précédente. Avec « dist\* »,
; Inno embarquait ces fichiers — et échouait même en cours de compression si l'un d'eux
; disparaissait entre-temps (« Le fichier spécifié est introuvable »). Ici, rien de tout ça
; n'est possible : ce qui n'est pas nommé n'est pas livré.
Source: "..\dist\BTOptimizer.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: app
; Composants natifs/satellites que .NET ne peut PAS embarquer dans le fichier unique.
Source: "..\dist\Microsoft.Windows.SDK.NET.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist; Components: app
Source: "..\dist\WinRT.Runtime.dll";             DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist; Components: app
Source: "..\dist\amd64\*";                       DestDir: "{app}\amd64"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist; Components: app
Source: "..\dist\fr\*";                          DestDir: "{app}\fr";    Flags: ignoreversion recursesubdirs skipifsourcedoesntexist; Components: app
#else
; ---- Publication ÉCLATÉE (dépendante du runtime, ou autonome non compressée) --------------
; ANTI-FUITE : tout ce qui n'est pas binaire est exclu — données du développeur (mémoire du
; Copilote, faits appris, journal, jeton de mise à jour), symboles de débogage et sources.
; « bt-*.md » manquait : bt-appris.md (conversations apprises) partait chez TOUS les
; utilisateurs. Le contrôle BT_RELEASE vérifie ce dossier avant chaque publication.
Source: "..\dist\*"; DestDir: "{app}"; \
  Excludes: "bt-*.txt,bt-*.csv,bt-*.md,bt-*.nip,bt-*.json,bt-etat\*,bt-savoir\*,bt-gamecache\*,*.pdb,*.etl,*.cs,*.log"; \
  Flags: ignoreversion recursesubdirs createallsubdirs; Components: app
#endif

; Profil de capture latence DPC/ISR (utilisé par la mesure ETW).
Source: "..\tools\dpc-trace.wprp"; DestDir: "{app}\tools"; Flags: ignoreversion skipifsourcedoesntexist; Components: app

; Composant NVIDIA optionnel. nvidiaProfileInspector est un outil tiers :
; vérifiez ses droits de redistribution avant toute diffusion commerciale.
; (les symboles de débogage de l'outil tiers ne servent à personne : on ne les livre pas)
Source: "..\tools\npi\*"; DestDir: "{app}\tools\npi"; Excludes: "*.pdb"; \
  Flags: ignoreversion recursesubdirs skipifsourcedoesntexist; Components: nvidia

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; L'app a un manifeste requireAdministrator. Une entree postinstall s'execute par
; defaut avec le jeton NON eleve de l'utilisateur d'origine -> CreateProcess echoue
; (code 740). runascurrentuser la lance avec le jeton (deja eleve) de l'installateur.
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName}"; WorkingDir: "{app}"; \
  Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Nettoie les fichiers créés par l'app après coup (sauvegardes/état/traces).
Type: files;      Name: "{app}\bt-*.txt"
Type: files;      Name: "{app}\bt-*.csv"
Type: files;      Name: "{app}\bt-*.nip"
Type: files;      Name: "{app}\bt-*.md"
Type: files;      Name: "{app}\tools\trace-*.etl"
Type: files;      Name: "{app}\tools\dpcisr-*.txt"
; Photos quotidiennes de l'etat du systeme (« ca marchait hier ») : dossier cree par l'app.
Type: filesandordirs; Name: "{app}\bt-etat"
Type: dirifempty; Name: "{app}\tools\npi"
Type: dirifempty; Name: "{app}\tools"
Type: dirifempty; Name: "{app}"
; Dossier de repli utilise quand « Program Files » n'est pas accessible en ecriture.
Type: filesandordirs; Name: "{localappdata}\ONYX"

[Code]
// La vérification du runtime .NET ne sert QUE pour une publication dépendante du runtime.
// En mode AUTONOME (self-contained), tout est embarqué -> aucune vérification nécessaire.
#ifndef SelfContained
// Détecte un runtime .NET Desktop 10.x (x64) installé.
function HasNet10Desktop(): Boolean;
var
  Base: string;
  FR: TFindRec;
begin
  Result := False;
  Base := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Base + '\*', FR) then
  try
    repeat
      if (FR.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        if Copy(FR.Name, 1, 3) = '10.' then
          Result := True;
    until Result or (not FindNext(FR));
  finally
    FindClose(FR);
  end;
end;

// Consentement IA écrit APRÈS la copie des fichiers : selon la case « Installer le cerveau IA
// local », on pré-accorde (bt-ia-consent.txt → l'app installe Ollama + le modèle adapté au 1er
// lancement, sans re-demander) ou on refuse durablement (bt-ia-off.txt → l'app n'insiste jamais).
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('installia') then
    begin
      SaveStringToFile(ExpandConstant('{app}\bt-ia-consent.txt'),
        'Cerveau IA local accepte via l''installateur. L''app installe le modele adapte au 1er lancement.' + #13#10, False);
      DeleteFile(ExpandConstant('{app}\bt-ia-off.txt'));
    end
    else
    begin
      SaveStringToFile(ExpandConstant('{app}\bt-ia-off.txt'),
        'Cerveau IA local refuse via l''installateur.' + #13#10, False);
      DeleteFile(ExpandConstant('{app}\bt-ia-consent.txt'));
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if HasNet10Desktop() then
    Exit;

  case MsgBox('Le .NET Desktop Runtime 10 (x64) est requis et ne semble pas installé.' + #13#10 +
              'ONYX ne pourra pas démarrer sans lui.' + #13#10#13#10 +
              '« Oui »  : ouvrir la page de téléchargement (rubrique « .NET Desktop Runtime »),' + #13#10 +
              '             installe le runtime puis relance ce programme.' + #13#10 +
              '« Non »  : installer quand même.' + #13#10 +
              '« Annuler » : arrêter.',
              mbConfirmation, MB_YESNOCANCEL) of
    IDYES:
      begin
        ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0',
                  '', '', SW_SHOW, ewNoWait, ErrorCode);
        Result := False;
      end;
    IDCANCEL:
      Result := False;
  end;
end;
#endif
