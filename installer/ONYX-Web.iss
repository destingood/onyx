; ============================================================================
;  Installateur EN LIGNE d'ONYX (Inno Setup 6.3+) : « ONYX-Installation.exe »
;
;  Un petit programme (2 Mo) à donner à télécharger : il lit sur GitHub quelle est la
;  dernière version, vérifie la place sur le disque, télécharge l'installateur complet,
;  contrôle son empreinte SHA256, puis l'installe sans autre question.
;  Il ne dépend d'aucune version : le même fichier installe toujours la dernière.
;
;  Compilation :  ISCC.exe ONYX-Web.iss            ->  Output\ONYX-Installation.exe
;  Essai sans droits d'administrateur, contre un serveur local, sans rien installer :
;                 ISCC.exe /DESSAI ONYX-Web.iss    ->  Output\ONYX-Installation-essai.exe
;                 ONYX-Installation-essai.exe /VERYSILENT /SUPPRESSMSGBOXES /LOG=essai.log
;                     [/BASE=http://127.0.0.1:8765/] [/LIBRE=octets libres simulés]
;
;  À publier avec CHAQUE release, à côté de l'installateur complet :
;    · ONYX-Installation.exe  (ce programme — son nom ne contient pas « setup » :
;                              la mise à jour intégrée d'ONYX ne doit pas le prendre) ;
;    · onyx-derniere.ini      (écrit par manifeste.ps1 : version, fichier, taille, SHA256).
;
;  Ce qu'il ne fait pas, par choix : aucune statistique d'installation, aucun identifiant
;  de machine, aucun programme proposé en plus. Une seule adresse est contactée : GitHub.
; ============================================================================

#define AppName "ONYX"

[Setup]
; Identifiant PROPRE à ce programme : il n'installe rien lui-même et ne doit pas se
; confondre avec l'entrée de désinstallation d'ONYX (celle de l'installateur complet).
AppId={{6F0C1A52-7D3E-4B8A-9C41-2E5D7A90B3F4}
AppName={#AppName}
AppVersion=1.0
AppVerName=Installation d'{#AppName}
AppPublisher=ONYX
AppPublisherURL=https://github.com/destingood/onyx
VersionInfoVersion=1.0.0.0
VersionInfoDescription=Installation en ligne d'ONYX
CreateAppDir=no
Uninstallable=no
OutputDir=Output
SetupIconFile=..\src\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=LICENSE.txt
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableReadyPage=no
#ifdef ESSAI
OutputBaseFilename=ONYX-Installation-essai
PrivilegesRequired=lowest
#else
OutputBaseFilename=ONYX-Installation
PrivilegesRequired=admin
; ONYX ouvert : l'assistant demande de le fermer avant d'aller plus loin.
AppMutex=BTOptimizer_SingleInstance
#endif

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
french.WelcomeLabel1=Installation d'ONYX
french.WelcomeLabel2=Cet assistant télécharge la dernière version d'ONYX (environ 60 Mo) et l'installe sur ce PC.%n%nIl faut une connexion à Internet. Rien d'autre n'est installé et aucune donnée ne quitte ce PC.%n%nONYX déjà installé : il est mis à jour, tes réglages et ton journal sont conservés.
french.ReadyLabel1=Prêt à télécharger et installer ONYX
french.ReadyLabel2a=Clique sur Installer pour télécharger ONYX depuis GitHub, contrôler le fichier reçu, puis l'installer.
french.ReadyLabel2b=Clique sur Installer pour télécharger ONYX depuis GitHub, contrôler le fichier reçu, puis l'installer.
french.FinishedHeadingLabel=ONYX est installé
french.FinishedLabelNoIcons=ONYX est installé sur ce PC. Tu le retrouves dans le menu Démarrer.
french.FinishedLabel=ONYX est installé sur ce PC. Tu le retrouves dans le menu Démarrer.

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Run]
; ONYX demande les droits d'administrateur : « runascurrentuser » le lance avec le jeton,
; déjà élevé, de cet assistant (sinon le lancement échoue, code 740).
Filename: "{code:CheminOnyx}"; Description: "Lancer ONYX"; \
  Flags: nowait postinstall skipifsilent runascurrentuser; Check: OnyxPret

[Code]
const
  Depot = 'https://github.com/destingood/onyx-releases/releases/';
  CleOnyx = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{2B539D2F-3B49-466B-B095-FEB9A1123E65}_is1';
  Chiffres = '0123456789';
  Lettres = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ';

var
  PageTelechargement: TDownloadWizardPage;
  PageInstallation: TOutputMarqueeProgressWizardPage;
  ManifesteLu, Telecharge, Installe: Boolean;
  MVersion, MEtiquette, MFichier, MSha256: String;
  MTaille: Int64;

// ---------------------------------------------------------------- adresses

function UrlManifeste: String;
begin
#ifdef ESSAI
  Result := ExpandConstant('{param:BASE|http://127.0.0.1:8765/}') + 'onyx-derniere.ini';
#else
  // « latest » : GitHub renvoie vers la dernière version finale publiée.
  Result := Depot + 'latest/download/onyx-derniere.ini';
#endif
end;

function UrlInstallateur: String;
begin
#ifdef ESSAI
  Result := ExpandConstant('{param:BASE|http://127.0.0.1:8765/}') + MFichier;
#else
  // Adresse de LA version annoncée par le manifeste, pas « latest » : si une version sort
  // entre les deux téléchargements, le fichier reste celui dont on a l'empreinte.
  Result := Depot + 'download/' + MEtiquette + '/' + MFichier;
#endif
end;

// ---------------------------------------------------------------- manifeste

// Non vide, pas trop long, et fait des seuls caractères permis : ces valeurs viennent du
// réseau et finissent dans une adresse et un nom de fichier.
function Sur(const S, Permis: String; Max: Integer): Boolean;
var
  I: Integer;
begin
  Result := (Length(S) > 0) and (Length(S) <= Max);
  if Result then
    for I := 1 to Length(S) do
      if Pos(S[I], Permis) = 0 then begin
        Result := False;
        Break;
      end;
end;

function ManifesteValide: Boolean;
begin
  Result := Sur(MVersion, Chiffres + '.', 20)
        and Sur(MEtiquette, Lettres + Chiffres + '.-', 30)
        and Sur(MFichier, Lettres + Chiffres + '.-', 60)
        and (Pos('ONYX-Setup-', MFichier) = 1)
        and (CompareText(Copy(MFichier, Length(MFichier) - 3, 4), '.exe') = 0)
        and (Pos('..', MFichier) = 0) and (Pos('..', MEtiquette) = 0)
        and Sur(MSha256, Chiffres + 'abcdefABCDEF', 64) and (Length(MSha256) = 64)
        and (MTaille > 1048576) and (MTaille < 2147483647);
end;

function LitManifeste(var Erreur: String): Boolean;
var
  Ini: String;
begin
  Result := ManifesteLu;
  if Result then Exit;
  Erreur := '';
  Ini := ExpandConstant('{tmp}\onyx-derniere.ini');
  DeleteFile(Ini);
  try
    DownloadTemporaryFile(UrlManifeste, 'onyx-derniere.ini', '', nil);
  except
    Erreur := 'Impossible de joindre le serveur de téléchargement d''ONYX. Vérifie la connexion à Internet, puis réessaie.'
            + #13#10#13#10 + GetExceptionMessage;
    Exit;
  end;
  MVersion := Trim(GetIniString('ONYX', 'Version', '', Ini));
  MEtiquette := Trim(GetIniString('ONYX', 'Etiquette', '', Ini));
  MFichier := Trim(GetIniString('ONYX', 'Fichier', '', Ini));
  MSha256 := Trim(GetIniString('ONYX', 'Sha256', '', Ini));
  MTaille := StrToInt64Def(Trim(GetIniString('ONYX', 'Taille', '', Ini)), 0);
  if not ManifesteValide then begin
    Erreur := 'Les informations de version reçues sont illisibles. Réessaie dans quelques minutes.';
    Exit;
  end;
  Log('ONYX ' + MVersion + ' : ' + MFichier + ', ' + IntToStr(MTaille) + ' octets, SHA256 ' + MSha256);
  ManifesteLu := True;
  Result := True;
end;

// ---------------------------------------------------------------- place sur le disque

// L'installateur, l'application qu'il pose (à peu près deux fois sa taille) et 200 Mo de
// marge : même règle que la mise à jour intégrée (MajLogique.EspaceRequis).
function EspaceRequis: Int64;
begin
  Result := MTaille * 3 + 209715200;
end;

function ManqueDePlace(const Chemin: String; var Erreur: String): Boolean;
var
  Libre, Total: Int64;
begin
  Result := False;
  if not GetSpaceOnDisk64(Chemin, Libre, Total) then Exit;   // inconnu : on ne bloque pas sur un doute
#ifdef ESSAI
  Libre := StrToInt64Def(ExpandConstant('{param:LIBRE|}'), Libre);
#endif
  if Libre >= EspaceRequis then Exit;
  Erreur := 'Pas assez de place sur le disque ' + ExtractFileDrive(Chemin) + ' : il faut '
          + IntToStr(EspaceRequis div 1048576) + ' Mo libres, il en reste ' + IntToStr(Libre div 1048576) + '.'
          + #13#10 + 'Rien n''a été téléchargé. Libère de la place, puis réessaie.';
  Result := True;
end;

// ---------------------------------------------------------------- téléchargement

// Le fichier arrive dans le dossier temporaire de l'assistant (protégé, supprimé à la
// fin) ; son empreinte SHA256 est comparée à celle du manifeste, sinon il est refusé.
function TelechargeInstallateur(AvecPage: Boolean; var Erreur: String): Boolean;
begin
  Result := Telecharge;
  if Result then Exit;
  Erreur := '';
  if ManqueDePlace(ExpandConstant('{tmp}'), Erreur) then Exit;
  if ManqueDePlace(ExpandConstant('{commonpf}'), Erreur) then Exit;
  if AvecPage then begin
    PageTelechargement.Clear;
    PageTelechargement.Add(UrlInstallateur, MFichier, MSha256);
    PageTelechargement.Show;
    try
      try
        PageTelechargement.Download;
        Telecharge := True;
      except
        if PageTelechargement.AbortedByUser then
          Log('Téléchargement annulé.')
        else
          Erreur := 'Le téléchargement d''ONYX a échoué : rien n''a été installé.' + #13#10#13#10 + GetExceptionMessage;
      end;
    finally
      PageTelechargement.Hide;
    end;
  end else begin
    try
      DownloadTemporaryFile(UrlInstallateur, MFichier, MSha256, nil);
      Telecharge := True;
    except
      Erreur := 'Le téléchargement d''ONYX a échoué : rien n''a été installé.' + #13#10#13#10 + GetExceptionMessage;
    end;
  end;
  if Telecharge then Log('Installateur téléchargé et contrôlé : ' + MFichier);
  Result := Telecharge;
end;

// ---------------------------------------------------------------- assistant

procedure InitializeWizard;
begin
  PageTelechargement := CreateDownloadPage('Téléchargement d''ONYX',
    'La dernière version est téléchargée depuis GitHub, puis contrôlée.', nil);
  PageTelechargement.ShowBaseNameInsteadOfUrl := True;
  PageInstallation := CreateOutputMarqueeProgressPage('Installation d''ONYX',
    'ONYX s''installe. Cela prend environ une minute.');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Erreur: String;
begin
  Result := True;
  // La version est lue avant la page « Prêt », qui l'affiche avec la place nécessaire.
  if (CurPageID = wpSelectTasks) or (CurPageID = wpReady) then
    Result := LitManifeste(Erreur);
  // Le téléchargement a lieu ici : en cas d'échec on reste sur la page, « Installer » réessaie.
  if Result and (CurPageID = wpReady) then
    Result := TelechargeInstallateur(True, Erreur);
  if (not Result) and (Erreur <> '') then
    SuppressibleMsgBox(Erreur, mbCriticalError, MB_OK, IDOK);
end;

function VersionCourte: String;
begin
  Result := MEtiquette;
  if (Length(Result) > 1) and (Result[1] = 'v') then Delete(Result, 1, 1);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  if ManifesteLu then
    Result := 'Version à installer :' + NewLine + Space + 'ONYX ' + VersionCourte + NewLine + NewLine
            + 'Téléchargement :' + NewLine + Space + IntToStr((MTaille + 1048575) div 1048576) + ' Mo, depuis GitHub' + NewLine + NewLine
            + 'Place nécessaire sur le disque :' + NewLine + Space + IntToStr(EspaceRequis div 1048576) + ' Mo' + NewLine + NewLine;
  Result := Result + MemoTasksInfo;
end;

// L'installateur complet est lancé ici, sans fenêtre, et son code de sortie est lu : un
// échec s'affiche sur cette page, d'où l'on peut revenir en arrière et réessayer.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Erreur, Fichier, Params: String;
  Code: Integer;
begin
  Result := '';
  // Repli : installation sans assistant, où la page « Prêt » n'a pas fait le téléchargement.
  if not LitManifeste(Erreur) then begin Result := Erreur; Exit; end;
  if not TelechargeInstallateur(False, Erreur) then begin
    if Erreur = '' then Erreur := 'Téléchargement annulé : rien n''a été installé.';
    Result := Erreur;
    Exit;
  end;
  Fichier := ExpandConstant('{tmp}\') + MFichier;
  // MERGETASKS : seul le raccourci du Bureau suit le choix fait ici ; le reste garde les
  // choix par défaut de l'installateur complet, ou ceux de l'installation précédente.
  Params := '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /MERGETASKS="';
  if not WizardIsTaskSelected('desktopicon') then Params := Params + '!';
  Params := Params + 'desktopicon"';
#ifdef ESSAI
  Log('ESSAI : installateur contrôlé, NON lancé : ' + Fichier + ' ' + Params);
  Installe := FileExists(Fichier);
#else
  PageInstallation.Show;
  try
    PageInstallation.Animate;
    if not Exec(Fichier, Params, '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then
      Result := 'L''installateur d''ONYX n''a pas pu démarrer : ' + SysErrorMessage(Code)
    else if Code <> 0 then
      Result := 'L''installation d''ONYX n''a pas abouti (code ' + IntToStr(Code) + '). Tu peux revenir en arrière et réessayer.'
    else
      Installe := True;
  finally
    PageInstallation.Hide;
  end;
#endif
end;

// ---------------------------------------------------------------- lancement d'ONYX

function CheminOnyx(Param: String): String;
var
  Dossier: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, CleOnyx, 'InstallLocation', Dossier)
     or RegQueryStringValue(HKLM32, CleOnyx, 'InstallLocation', Dossier) then
    Result := AddBackslash(Dossier) + 'BTOptimizer.exe';
end;

function OnyxPret: Boolean;
begin
#ifdef ESSAI
  Result := False;   // l'essai n'installe rien : il ne lance rien non plus
#else
  Result := Installe and FileExists(CheminOnyx(''));
#endif
end;
