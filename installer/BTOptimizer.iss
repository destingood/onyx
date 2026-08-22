; ============================================================================
;  Installateur ONYX (Inno Setup 6.3+)
;
;  Compilation (le plus simple) : double-clic sur ..\Build-Installer.bat
;  Manuel :  1) publie l'app :  dotnet publish -c Release -o dist
;            2) compile ce script : "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" BTOptimizer.iss
;  Résultat :  installer\Output\BTOptimizer-Setup-<version>.exe
;
;  Source compilée : ..\build\stage (préparé par les scripts de build), sinon ..\dist.
;  Deux modes de diffusion, détectés AUTOMATIQUEMENT selon le contenu de ce dossier :
;   • Dépendant du runtime (Build-Installer.bat) : ~16 Mo, exige .NET Desktop 10 (x64)
;     côté client -> le script le vérifie et propose la page de téléchargement s'il manque.
;   • AUTONOME / self-contained (Build-Standalone.bat) : ~120 Mo, runtime embarqué,
;     s'installe et tourne SANS aucun prérequis. Idéal pour une diffusion grand public.
; ============================================================================

#define AppName "ONYX"
#define AppExe "BTOptimizer.exe"
; DOSSIER SOURCE DE LA LIVRAISON.
; On ne compile PLUS depuis « dist\ » : c'est le dossier où le développeur EXÉCUTE l'app.
; Il bouge donc pendant la compilation (l'app y écrit son état, l'antivirus y intervient,
; une seconde compilation lancée en parallèle commence par le vider). Inno liste les
; fichiers au début, puis les compresse ~40 s plus tard : si l'un d'eux disparaît entre
; temps, la compilation s'arrête sur « Le fichier spécifié est introuvable » — à un endroit
; DIFFÉRENT à chaque fois, ce qui rendait la panne incompréhensible.
; « build\stage » est créé par les scripts de build juste avant l'appel à ISCC, ne contient
; QUE ce qui doit être livré, et rien d'autre ne l'utilise.
#ifexist "..\build\stage\BTOptimizer.exe"
  #define Src "..\build\stage"
#else
  ; Repli : compilation manuelle après un simple « dotnet publish -o dist ».
  #define Src "..\dist"
#endif

; La version est lue automatiquement depuis le binaire publié (évite toute dérive).
#ifexist Src + "\BTOptimizer.exe"
  #define AppVersion GetVersionNumbersString(Src + "\BTOptimizer.exe")
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
#ifexist Src + "\coreclr.dll"
  #define SelfContained
#endif
#ifexist Src + "\BTOptimizer.exe"
  #if !defined(SelfContained) && FileSize(Src + "\BTOptimizer.exe") > 40000000
    #define SelfContained
  #endif
#endif
; Rappel a la compilation : on sait ainsi tout de suite quel type de setup on fabrique.
#ifdef SelfContained
  #pragma message "Mode AUTONOME detecte -> aucun runtime .NET exige du client."
#else
  #pragma message "Mode DEPENDANT DU RUNTIME -> le setup verifiera .NET Desktop 10 x64."
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

[InstallDelete]
; ON NE POSE JAMAIS UNE VERSION SUR LES RESTES DE LA PRÉCÉDENTE.
;
; ONYX se livre sous deux formes : AUTONOME (le runtime .NET est copié dans le dossier de l'app)
; et DÉPENDANT DU RUNTIME (l'app utilise le .NET installé sur la machine). Installer la seconde
; par-dessus la première laissait un dossier hybride : le runtime complet de l'ancienne version
; restait à côté du nouveau binaire.
;
; Et ce n'était pas un simple encombrement. « BTOptimizer.exe » cherche « hostfxr.dll » À CÔTÉ DE
; LUI : s'il en trouve un, il se croit autonome et démarre avec le runtime du dossier — celui de
; l'ancienne version. Le résultat était le pire possible pour l'utilisateur : Windows affichait
; « You must install or update .NET to run this application » sur une machine où .NET était
; PARFAITEMENT installé. Message insoluble : on peut réinstaller .NET dix fois, le fichier fautif
; est dans le dossier de l'app.
;
; On efface donc les DLL et le dossier « runtimes » AVANT la copie ; [Files] repose aussitôt les
; 27 DLL réellement nécessaires. Les données de l'utilisateur (bt-*.txt, licence Pro), le
; désinstalleur (unins000.*) et les outils tiers (tools\) ne sont pas touchés : ils ne sont ni
; des .dll à la racine, ni le dossier « runtimes ».
Type: files;          Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\runtimes"
Type: files;          Name: "{app}\createdump.exe"

[Files]
; Binaires .NET 10 (produits par « dotnet publish -o dist »).
; On exclut les fichiers d'état générés à l'exécution et les symboles de débogage.
; ANTI-FUITE : tout ce qui n'est pas le binaire est exclu explicitement — donnees de l'utilisateur
; developpeur (memoire du Copilote, faits appris, journal, jeton de mise a jour), symboles de
; debogage et sources. « bt-*.md » manquait : bt-appris.md (conversations apprises) partait chez
; TOUS les utilisateurs. Le probe BT_RELEASE verifie ce dossier avant chaque publication.
; ANTI-FUITE — exclusion par PRÉFIXE, et non par extension.
; Le dossier de publication est aussi celui où le développeur SE SERT de l'app : s'y
; accumulent la mémoire du Copilote, les faits appris, le journal, la licence, le jeton de
; mise à jour. L'ancienne liste énumérait des extensions (« bt-*.txt, bt-*.csv, bt-*.md… ») :
; il suffisait qu'un nouveau fichier d'état apparaisse avec une extension non prévue pour
; qu'il parte chez TOUS les utilisateurs — c'est précisément ce qui était arrivé à
; bt-appris.md, qui contenait de vraies conversations. « bt-* » couvre les fichiers ET les
; dossiers, actuels comme futurs.
;
; Une liste explicite (nom par nom) a aussi été essayée : elle a produit un setup AMPUTÉ de
; Microsoft.Diagnostics.Tracing.TraceEvent.dll, que .NET ne peut pas embarquer — la mesure
; de latence DPC/ISR aurait planté chez le client. Un joker filtré ne peut pas, lui,
; oublier un binaire.
Source: "{#Src}\*"; DestDir: "{app}"; \
  Excludes: "bt-*,*.pdb,*.cs,*.csproj,*.sln,*.etl,*.log,*.pfx,*.tmp"; \
  Flags: ignoreversion recursesubdirs createallsubdirs; Components: app

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
// Un dossier « Microsoft.WindowsDesktop.App » contient-il une version 10.x ?
// Isolé de HasNet10Desktop pour pouvoir interroger PLUSIEURS emplacements : .NET ne s'installe
// pas toujours dans « Program Files ». Un chemin vide n'est jamais interrogé (GetEnv rend '' quand
// la variable n'existe pas, et FindFirst('\*') partirait explorer la racine du disque).
function Dossier10x(Base: string): Boolean;
var
  FR: TFindRec;
begin
  Result := False;
  if Base = '' then Exit;
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

// Détecte un runtime .NET Desktop 10.x installé.
//
// UNE SEULE SOURCE NE SUFFIT PAS, et se tromper ici coûte cher : réclamer .NET à quelqu'un qui l'a
// déjà, c'est l'envoyer réinstaller pour rien un runtime de 60 Mo — et lui donner l'impression que
// le programme ne sait pas ce qu'il dit. On interroge donc, dans l'ordre :
//   · Program Files 64 bits, puis celui de l'installation en cours ;
//   · DOTNET_ROOT et ProgramW6432, pour les installations hors des chemins par défaut ;
//   · le registre du programme d'installation de .NET, en vue 64 bits ET 32 bits.
// Une seule réponse positive suffit ; aucune ne peut créer de faux négatif chez les autres.
function HasNet10Desktop(): Boolean;
var
  Suffixe: string;
begin
  Suffixe := '\dotnet\shared\Microsoft.WindowsDesktop.App';
  Result := Dossier10x(ExpandConstant('{commonpf}') + Suffixe);
  if not Result then Result := Dossier10x(ExpandConstant('{commonpf64}') + Suffixe);
  if not Result then Result := Dossier10x(ExpandConstant('{sd}') + '\Program Files' + Suffixe);
  if not Result then Result := Dossier10x(GetEnv('ProgramW6432') + Suffixe);
  if not Result then Result := Dossier10x(GetEnv('DOTNET_ROOT') + '\shared\Microsoft.WindowsDesktop.App');
  if not Result then
    Result := RegKeyExists(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App')
           or RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
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

  // NE JAMAIS BLOQUER SUR UN DOUTE. Cette détection lit des dossiers et des clés : elle peut se
  // tromper (installation hors des chemins habituels, droits de lecture refusés), et se tromper
  // dans ce sens-là arrêtait l'installation de quelqu'un qui avait pourtant .NET. L'inverse ne
  // coûte rien : si le runtime manque VRAIMENT, Windows le dira lui-même au premier lancement,
  // avec un message exact et un lien de téléchargement. « Continuer » est donc le bouton par
  // défaut — appuyer sur Entrée installe.
  if MsgBox('ONYX a besoin du .NET Desktop Runtime 10 (x64), et n''a pas réussi à le trouver sur ce PC.' + #13#10#13#10 +
            'Ce n''est pas une certitude : si tu l''as installé ailleurs que dans « Program Files »,' + #13#10 +
            'il est probablement là et tout ira bien.' + #13#10#13#10 +
            '« Oui »  : continuer l''installation (recommandé).' + #13#10 +
            '« Non »  : ouvrir la page de téléchargement et arrêter là.',
            mbInformation, MB_YESNO) = IDNO then
  begin
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0',
              '', '', SW_SHOW, ewNoWait, ErrorCode);
    Result := False;
  end;
end;
#endif
