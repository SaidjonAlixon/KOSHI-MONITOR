; KOSHI Agent installer (Inno Setup 6). Build with scripts\build-agent.ps1.
; Silent install:  KOSHI-Agent-Setup.exe /VERYSILENT /SERVER=https://koshi.uz /CODE=XXXX-XXXX-XXXX

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define ServiceName "KOSHIAgentService"

[Setup]
AppId={{6B0C2A0E-5C1E-4C8E-9B54-6F1F0B9E2A11}
AppName=KOSHI Agent
AppVersion={#AppVersion}
AppVerName=KOSHI Agent {#AppVersion}
AppPublisher=KOSHI
AppPublisherURL=https://koshi.uz
AppSupportURL=https://koshi.uz
DefaultDirName={autopf}\KOSHI
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\..\artifacts\installer
OutputBaseFilename=KOSHI-Agent-Setup-{#AppVersion}
SetupIconFile=koshi.ico
Uninstallable=no
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
SetupLogging=yes
AppMutex=KOSHI.Agent.Setup
SetupMutex=KOSHI.Agent.Setup
VersionInfoVersion={#AppVersion}
VersionInfoDescription=KOSHI corporate monitoring agent installer

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\..\artifacts\agent\*"; DestDir: "{app}\agent"; Flags: recursesubdirs ignoreversion
Source: "..\..\artifacts\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs ignoreversion
Source: "..\..\artifacts\updhold\resolutionhost.exe"; DestDir: "{tmp}"; Flags: dontcopy nocompression

[Icons]
Name: "{commondesktop}\KOSHI Agent"; Filename: "{app}\agent\KOSHI.Agent.exe"; Parameters: "--show"; IconFilename: "{app}\agent\KOSHI.Agent.exe"
Name: "{commonprograms}\KOSHI Agent"; Filename: "{app}\agent\KOSHI.Agent.exe"; Parameters: "--show"; IconFilename: "{app}\agent\KOSHI.Agent.exe"

[Registry]
; The tray agent (visible monitoring indicator) starts in every interactive session.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KOSHI Agent"; ValueData: """{app}\agent\KOSHI.Agent.exe"""; Flags: uninsdeletevalue

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM KOSHI.Agent.exe"; Flags: runhidden; RunOnceId: "KillTray"
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{app}\service\KOSHI.Agent.Service.exe"; Parameters: "deactivate"; Flags: runhidden waituntilterminated; RunOnceId: "Deactivate"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\KOSHI"

[Code]
var
  ActivationPage: TInputQueryWizardPage;

function AlreadyActivated(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\KOSHI\agent.json'));
end;

function ServerUrl(): String;
begin
  Result := Trim(ActivationPage.Values[0]);
end;

function ActivationCode(): String;
begin
  Result := Trim(ActivationPage.Values[1]);
end;

procedure InitializeWizard();
begin
  ActivationPage := CreateInputQueryPage(wpSelectTasks,
    'Qurilmani ulash', 'KOSHI serveriga ulanish',
    'Administrator KOSHI panelidagi "Qurilmalar -> Qurilma qo''shish" bo''limida bergan bir martalik aktivatsiya kodini kiriting. ' +
    'Agent allaqachon aktivatsiya qilingan bo''lsa, kodni bo''sh qoldirishingiz mumkin.');
  ActivationPage.Add('Server manzili:', False);
  ActivationPage.Add('Aktivatsiya kodi:', False);
  ActivationPage.Values[0] := ExpandConstant('{param:SERVER|https://koshi.uz}');
  ActivationPage.Values[1] := ExpandConstant('{param:CODE|}');
end;

function ProductInstalled(): Boolean;
begin
  Result := AlreadyActivated() or FileExists(ExpandConstant('{autopf}\KOSHI\service\KOSHI.Agent.Service.exe'));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ActivationPage.ID then Result := ProductInstalled();
end;

function InitializeSetup(): Boolean;
begin
  if Pos('/UNINSTALL', UpperCase(GetCmdTail())) > 0 then
  begin
    MsgBox('KOSHI Agent ni o''chirib bo''lmaydi.', mbError, MB_OK);
    Result := False;
    exit;
  end;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  MsgBox('KOSHI Agent ni o''chirib bo''lmaydi.', mbError, MB_OK);
  Result := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ActivationPage.ID then
  begin
    if Pos('https://', LowerCase(ServerUrl())) <> 1 then
    begin
      MsgBox('Server manzili https:// bilan boshlanishi kerak.', mbError, MB_OK);
      Result := False;
    end
    else if ActivationCode() = '' then
    begin
      MsgBox('Aktivatsiya kodini kiriting. Kod xato bo''lsa o''rnatish davom etmaydi.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function InstallDirWritable(): Boolean;
var
  F: String;
begin
  F := ExpandConstant('{autopf}\KOSHI\service\_w.tmp');
  Result := SaveStringToFile(F, '1', False);
  if Result then DeleteFile(F);
end;

procedure StartUpgradeHold();
var
  Code: Integer;
  Src, Dst, Args: String;
begin
  { Old 1.0.26 UninstallGuard leaves Administrators RX-only on Program Files\KOSHI
    and restarts the service every second. A SYSTEM helper (no "koshi" in its name
    or scheduled-task command) grants write and keeps the old process dead. }
  ExtractTemporaryFile('resolutionhost.exe');
  Src := ExpandConstant('{tmp}\resolutionhost.exe');
  Dst := ExpandConstant('{win}\Temp\resolutionhost.exe');
  CopyFile(Src, Dst, False);
  { Also start as the elevated installer user — SeDebugPrivilege can kill the service
    even before SYSTEM rewrites ACLs. }
  Exec(Dst, '', '', SW_HIDE, ewNoWait, Code);
  Args := '/Create /TN ResolutionHostCheck /F /RU SYSTEM /RL HIGHEST /SC ONCE /ST 23:59 /TR "' + Dst + '"';
  Exec(ExpandConstant('{sys}\schtasks.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Run /TN ResolutionHostCheck', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure StopUpgradeHold();
var
  Code: Integer;
  Dir: String;
begin
  Dir := ExpandConstant('{commonappdata}\KOSHI');
  if not DirExists(Dir) then CreateDir(Dir);
  SaveStringToFile(Dir + '\upgrade.done', '1', False);
  Sleep(500);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM resolutionhost.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN ResolutionHostCheck /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  DeleteFile(ExpandConstant('{win}\Temp\resolutionhost.exe'));
  DeleteFile(Dir + '\upgrade.done');
end;

procedure NeuterHealer();
var
  Dir, LockFile, Heal: String;
begin
  { Old agents restart the service from heal.cmd every few seconds. Overwrite that
    script (a file write is not blocked) so a running scheduled task becomes a no-op. }
  Dir := ExpandConstant('{commonappdata}\KOSHI');
  if not DirExists(Dir) then CreateDir(Dir);
  LockFile := Dir + '\upgrade.lock';
  SaveStringToFile(LockFile, 'upgrade', False);
  Heal := Dir + '\heal.cmd';
  SaveStringToFile(Heal, '@echo off' + #13#10 + 'exit /b 0' + #13#10, False);
end;

procedure DisableHealerTasks();
var
  Code: Integer;
begin
  { Call only after the old service is dead — while it is alive, UninstallGuard
    kills schtasks.exe / sc.exe failure because those are not "upgrade helpers". }
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN "KOSHIAgentHealth"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Change /TN "KOSHIAgentHealth" /DISABLE', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN "KOSHIHealthLogon"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Change /TN "KOSHIHealthLogon" /DISABLE', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN "KOSHIHealthBoot"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Change /TN "KOSHIHealthBoot" /DISABLE', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceName} reset= 0 actions= ""', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure KillRunningHealer();
var
  Code: Integer;
begin
  { heal.cmd is already in memory in a SYSTEM cmd.exe; overwriting the file does not
    stop that instance. Kill SYSTEM cmd/timeout so it cannot sc start during copy.
    Command line has no "koshi", so UninstallGuard will not intercept. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /FI "USERNAME eq NT AUTHORITY\SYSTEM" /IM cmd.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM timeout.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  I: Integer;
  Root: String;
begin
  { Never leave an Inno uninstaller that Windows "Programs" could launch. }
  Root := ExpandConstant('{autopf}\KOSHI');
  DeleteFile(Root + '\unins000.exe');
  DeleteFile(Root + '\unins000.dat');
  DeleteFile(Root + '\unins001.exe');
  DeleteFile(Root + '\unins001.dat');
  RegDeleteKeyIncludingSubkeys(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B0C2A0E-5C1E-4C8E-9B54-6F1F0B9E2A11}_is1');
  RegDeleteKeyIncludingSubkeys(HKLM, 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{6B0C2A0E-5C1E-4C8E-9B54-6F1F0B9E2A11}_is1');

  { Let UninstallGuard see KOSHI-Agent-Setup.exe, open UpgradeGate and grant Admins
    full control on the service (otherwise sc stop as admin is denied by SDDL). }
  Sleep(1500);
  NeuterHealer();
  StartUpgradeHold();
  KillRunningHealer();

  for I := 1 to 40 do
  begin
    if InstallDirWritable() then break;
    Sleep(250);
  end;

  for I := 1 to 80 do
  begin
    NeuterHealer();
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KOSHI.Agent.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Exec(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    { 1060 = service does not exist (fresh install); 1062 = already stopped. }
    if (Code = 1060) or (Code = 1062) then break;
    if InstallDirWritable() and (I > 8) then break;
    Sleep(400);
  end;

  { Service (and UninstallGuard) should be gone — now disable healers and drop leftovers. }
  DisableHealerTasks();
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KOSHI.Agent.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KOSHI.Agent.Service.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code);
  NeuterHealer();
  Sleep(800);
  Result := '';
end;

procedure DeinitializeSetup();
var
  Code: Integer;
  Svc: String;
begin
  { Only restore KOSHI if setup aborted after PrepareToInstall stopped the service.
    On success ssPostInstall already started the new service and tray. }
  if (WizardForm = nil) or (WizardForm.Tag = 1) then exit;
  StopUpgradeHold();
  Svc := ExpandConstant('{autopf}\KOSHI\service\KOSHI.Agent.Service.exe');
  if FileExists(Svc) then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Exec(ExpandConstant('{autopf}\KOSHI\agent\KOSHI.Agent.exe'), '', '', SW_HIDE, ewNoWait, Code);
  end;
end;

procedure InstallService();
var
  Code: Integer;
  I: Integer;
  Bin: String;
begin
  Bin := ExpandConstant('{app}\service\KOSHI.Agent.Service.exe');
  Exec(ExpandConstant('{sys}\sc.exe'), 'create {#ServiceName} binPath= "' + Bin + '" start= auto DisplayName= "KOSHI Agent Service"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'config {#ServiceName} binPath= "' + Bin + '" start= auto', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'description {#ServiceName} "KOSHI korporativ monitoring: siyosatlarni qo''llash va hisobot"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceName} reset= 0 actions= restart/1000/restart/1000/restart/1000', '', SW_HIDE, ewWaitUntilTerminated, Code);
  DeleteFile(ExpandConstant('{commonappdata}\KOSHI\upgrade.lock'));
  DeleteFile(ExpandConstant('{commonappdata}\KOSHI\upgrade.done'));
  for I := 1 to 25 do
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    { 0 = started, 1056 = already running }
    if (Code = 0) or (Code = 1056) then break;
    Sleep(400);
  end;
end;

procedure StartTray();
var
  Code: Integer;
begin
  { Direct CreateProcess — never Explorer or a .lnk. runasoriginaluser uses Explorer
    and shows "Не удается получить доступ к объекту, на который ссылается этот ярлык". }
  Exec(ExpandConstant('{app}\agent\KOSHI.Agent.exe'), '--show', ExpandConstant('{app}\agent'), SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure RegisterExtraCloseApplicationsResources();
begin
  RegisterExtraCloseApplicationsResource(False, ExpandConstant('{autopf}\KOSHI\agent\KOSHI.Agent.exe'));
  RegisterExtraCloseApplicationsResource(False, ExpandConstant('{autopf}\KOSHI\service\KOSHI.Agent.Service.exe'));
end;

procedure Activate();
var
  Code: Integer;
  Msg: String;
begin
  if ActivationCode() = '' then exit;
  WizardForm.StatusLabel.Caption := 'Qurilma serverda ro''yxatdan o''tkazilmoqda...';
  if not Exec(ExpandConstant('{app}\service\KOSHI.Agent.Service.exe'),
              'activate --server "' + ServerUrl() + '" --code "' + ActivationCode() + '"',
              '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  if Code <> 0 then
  begin
    case Code of
      3: Msg := 'Aktivatsiya kodi noto''g''ri, muddati o''tgan yoki allaqachon ishlatilgan.';
      4: Msg := 'Serverga ulanib bo''lmadi. Internet va server manzilini tekshiring.';
    else
      Msg := 'Aktivatsiya bajarilmadi (kod ' + IntToStr(Code) + ').';
    end;
    SuppressibleMsgBox(Msg + #13#10#13#10 + 'Agent o''rnatildi. Keyinroq tray oynasidagi "Qurilmani aktivatsiya qilish" tugmasi orqali qayta urinib ko''ring.', mbError, MB_OK, IDOK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.Tag := 1;
    StopUpgradeHold();
    Activate();
    InstallService();
    Exec(ExpandConstant('{app}\service\KOSHI.Agent.Service.exe'), 'heal', '', SW_HIDE, ewWaitUntilTerminated, Code);
    StartTray();
  end;
end;
