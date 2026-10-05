; Monitor Power 설치 파일 스크립트 (Inno Setup 6)
; build.ps1 -Package 로 만듭니다. 버전과 설치할 파일 폴더는 /DAppVersion, /DPackageDir 로 받습니다.

#define AppName "Monitor Power"
#define AppExe "MonitorPower.exe"
#define AppPublisher "jaehun6912"
#define AppUrl "https://github.com/jaehun6912/MonitorPower"
#define ToolUrl "https://www.nirsoft.net/utils/control_my_monitor.html"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PackageDir
  #define PackageDir "..\artifacts\package"
#endif

[Setup]
AppId={{9EDD9375-72AC-4D81-A933-3C358AB55553}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\MonitorPower
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=MonitorPower-setup-{#AppVersion}
SetupIconFile=..\src\MonitorPower.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
; 관리자 권한 없이 사용자 폴더에 설치한다(서명이 없어 UAC 창을 띄우지 않는 편이 낫다).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 실행 중이면 닫으라고 안내한다(프로그램의 중복 실행 방지 뮤텍스와 같은 이름).
AppMutex=Local\MonitorPower.SingleInstance

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; GroupDescription: "추가 작업:"; Flags: unchecked

[Files]
Source: "{#PackageDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 설치 중에 복사했거나 사용자가 넣은 ControlMyMonitor와 그 설정 파일
Type: files; Name: "{app}\ControlMyMonitor.exe"
Type: files; Name: "{app}\ControlMyMonitor.cfg"

[Code]
var
  ToolPage: TInputFileWizardPage;

function DotNet48Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DotNet48Installed() then
    Result := MsgBox(
      '이 프로그램을 실행하려면 .NET Framework 4.8이 필요합니다.' + #13#10 +
      'Windows 10 2019년 5월 업데이트(1903) 이후와 Windows 11에는 기본으로 들어 있습니다.' + #13#10#13#10 +
      '지금 설치를 계속할 수는 있지만, .NET Framework 4.8을 설치하기 전에는 프로그램이 실행되지 않습니다.' + #13#10#13#10 +
      '계속할까요?', mbConfirmation, MB_YESNO) = IDYES;
end;

// ControlMyMonitor는 NirSoft의 프로그램이라 함께 배포하지 않는다. 이미 받아 둔 파일이 있으면 골라서 설치 폴더로 복사한다.
procedure InitializeWizard();
var
  Beside: String;
begin
  ToolPage := CreateInputFilePage(wpSelectTasks,
    'ControlMyMonitor 연결',
    'Monitor Power는 NirSoft의 ControlMyMonitor로 모니터에 명령을 보냅니다.',
    '이미 받아 둔 ControlMyMonitor.exe를 고르면 설치 폴더로 복사합니다. 비워 두면 나중에 설치 폴더에 직접 넣어도 됩니다.' + #13#10#13#10 +
    '받는 곳: {#ToolUrl}');
  ToolPage.Add('ControlMyMonitor.exe 위치 (선택):', 'ControlMyMonitor|ControlMyMonitor.exe|실행 파일|*.exe', '.exe');
  Beside := ExpandConstant('{src}\ControlMyMonitor.exe');
  if FileExists(Beside) then
    ToolPage.Values[0] := Beside;
end;

// 다시 설치할 때 설치 폴더에 이미 있으면 묻지 않는다.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ToolPage.ID) and FileExists(ExpandConstant('{app}\ControlMyMonitor.exe'));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Path: String;
begin
  Result := True;
  if CurPageID <> ToolPage.ID then
    Exit;
  Path := Trim(ToolPage.Values[0]);
  if (Path <> '') and not FileExists(Path) then
  begin
    MsgBox('고른 파일을 찾을 수 없습니다. 다시 고르거나 칸을 비워 두세요.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Path: String;
begin
  if CurStep <> ssPostInstall then
    Exit;
  Path := Trim(ToolPage.Values[0]);
  if (Path = '') or not FileExists(Path) then
    Exit;
  if not FileCopy(Path, ExpandConstant('{app}\ControlMyMonitor.exe'), False) then
    MsgBox('ControlMyMonitor.exe를 설치 폴더로 복사하지 못했습니다.' + #13#10 + '설치 폴더에 직접 넣어 주세요: ' + ExpandConstant('{app}'), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Settings: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  // 조용히 제거할 때는 묻지 않고 사용자 설정을 그대로 둔다.
  if UninstallSilent then
    Exit;
  Settings := ExpandConstant('{userappdata}\MonitorPower');
  if not DirExists(Settings) then
    Exit;
  if MsgBox('설정 파일(마지막 모니터, 테마, 자동 새로고침 등)도 지울까요?' + #13#10#13#10 +
            '지우지 않으면 다시 설치했을 때 그대로 쓸 수 있습니다.', mbConfirmation, MB_YESNO) = IDYES then
    DelTree(Settings, True, True, True);
end;
