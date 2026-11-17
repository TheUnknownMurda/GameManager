; Game Manager installer (Inno Setup 6)
; Build: .\build.ps1   (or ISCC.exe /DAppVersion=1.0.0 installer\GestionnaireJeux.iss)

#define AppName "Game Manager"
#define AppExe "GestionnaireJeux.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef AppUrl
  #define AppUrl "https://github.com/TheUnknownMurda/GameManager"
#endif

[Setup]
AppId={{7647E0E9-CA55-46E5-B988-5114430D0F3C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
; Per-user install, no administrator rights needed (%LOCALAPPDATA%\Programs\GestionnaireJeux)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\GestionnaireJeux
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=GameManagerInstall
SetupIconFile=..\GestionnaireJeux\Assets\app.ico
WizardSmallImageFile=wizard-small.png
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.ShortcutName=Game Manager
french.ShortcutName=Gestionnaire de jeux
english.ShortcutComment=List and uninstall your installed games, by drive
french.ShortcutComment=Lister et désinstaller les jeux installés, par lecteur
english.DotNetMissing=Game Manager needs the Microsoft .NET Desktop Runtime (version 8 or later), which is not installed on this PC.%n%nDownload it from Microsoft (about 58 MB) and install it now?%n%nWindows will ask for administrator permission.
french.DotNetMissing=Game Manager a besoin du runtime Microsoft .NET Desktop (version 8 ou plus récente), qui n’est pas installé sur ce PC.%n%nLe télécharger depuis Microsoft (environ 58 Mo) et l’installer maintenant ?%n%nWindows vous demandera une autorisation administrateur.
english.DotNetFailed=The .NET runtime installation did not complete (code %1). The app will not start until it is installed: https://dotnet.microsoft.com/download/dotnet/8.0
french.DotNetFailed=L’installation du runtime .NET n’a pas abouti (code %1). L’application ne démarrera pas tant qu’il ne sera pas installé : https://dotnet.microsoft.com/download/dotnet/8.0
english.DotNetSkipped=Without the .NET Desktop Runtime, the app will not start. You can install it later: https://dotnet.microsoft.com/download/dotnet/8.0
french.DotNetSkipped=Sans le runtime .NET Desktop, l’application ne démarrera pas. Vous pourrez l’installer plus tard : https://dotnet.microsoft.com/download/dotnet/8.0

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{cm:ShortcutName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:ShortcutComment}"
Name: "{autodesktop}\{cm:ShortcutName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:ShortcutComment}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{cm:ShortcutName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  DotNetFile = 'windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

{ True when a Microsoft.WindowsDesktop.App runtime of version 8 or later is installed. }
function HasDesktopRuntime(): Boolean;
var
  FindRec: TFindRec;
  Base: String;
  Major, Dot: Integer;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  if FindFirst(Base + '*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Dot := Pos('.', FindRec.Name);
          if Dot > 1 then
          begin
            Major := StrToIntDef(Copy(FindRec.Name, 1, Dot - 1), 0);
            if Major >= 8 then
            begin
              Log('.NET Desktop runtime found: ' + FindRec.Name);
              Result := True;
              Break;
            end;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
  if not Result then
    Log('No .NET Desktop runtime >= 8 in ' + Base);
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or HasDesktopRuntime() then
    Exit;

  if SuppressibleMsgBox(CustomMessage('DotNetMissing'), mbConfirmation, MB_YESNO, IDNO) <> IDYES then
  begin
    SuppressibleMsgBox(CustomMessage('DotNetSkipped'), mbInformation, MB_OK, IDOK);
    Exit;
  end;

  DownloadPage.Clear;
  DownloadPage.Add(DotNetUrl, DotNetFile, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      DownloadPage.SetText(SetupMessage(msgWizardInstalling), '.NET Desktop Runtime');
      if not ShellExec('runas', ExpandConstant('{tmp}\') + DotNetFile, '/install /quiet /norestart', '', SW_SHOW,
                       ewWaitUntilTerminated, ResultCode) then
        SuppressibleMsgBox(FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(ResultCode)]), mbError, MB_OK, IDOK)
      else if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) and (ResultCode <> 1638) then
        SuppressibleMsgBox(FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(ResultCode)]), mbError, MB_OK, IDOK);
    except
      SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbError, MB_OK, IDOK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

{ First install: the app starts in the language chosen for the installer. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsFile, Lang: String;
begin
  if CurStep <> ssPostInstall then
    Exit;
  SettingsFile := ExpandConstant('{localappdata}\GestionnaireJeux\settings.json');
  if FileExists(SettingsFile) then
    Exit;
  if ActiveLanguage = 'french' then
    Lang := 'fr'
  else
    Lang := 'en';
  ForceDirectories(ExtractFileDir(SettingsFile));
  SaveStringToFile(SettingsFile, '{ "Language": "' + Lang + '" }', False);
end;
