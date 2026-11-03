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
VersionInfoVersion={#AppVersion}
VersionInfoDescription=KOSHI corporate monitoring agent installer

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\..\artifacts\agent\*"; DestDir: "{app}\agent"; Flags: recursesubdirs ignoreversion
Source: "..\..\artifacts\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs ignoreversion

[Registry]
; The tray agent (visible monitoring indicator) starts in every interactive session.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KOSHI Agent"; ValueData: """{app}\agent\KOSHI.Agent.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\agent\KOSHI.Agent.exe"; Parameters: "--show"; Flags: nowait postinstall runasoriginaluser skipifsilent; Description: "KOSHI Agent oynasini ochish"
Filename: "{app}\agent\KOSHI.Agent.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

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
    else if (ActivationCode() = '') and not AlreadyActivated() then
    begin
      Result := MsgBox('Aktivatsiya kodi kiritilmadi. Agent o''rnatiladi, lekin kod kiritilmaguncha monitoring ishlamaydi. Davom etilsinmi?', mbConfirmation, MB_YESNO) = IDYES;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  { Upgrade: stop the running agent and service so files can be replaced. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KOSHI.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
  Result := '';
end;

procedure InstallService();
var
  Code: Integer;
  Bin: String;
begin
  Bin := ExpandConstant('{app}\service\KOSHI.Agent.Service.exe');
  Exec(ExpandConstant('{sys}\sc.exe'), 'create {#ServiceName} binPath= "' + Bin + '" start= auto DisplayName= "KOSHI Agent Service"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'config {#ServiceName} binPath= "' + Bin + '" start= auto', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'description {#ServiceName} "KOSHI korporativ monitoring: siyosatlarni qo''llash va hisobot"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code);
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
begin
  if CurStep = ssPostInstall then
  begin
    Activate();
    InstallService();
  end;
end;
