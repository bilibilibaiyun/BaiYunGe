; BaiYunGe 白云歌 安装程序脚本 (Inno Setup 7)
; 由 007 生成 — 白云歌 v2.0.3

#define MyAppName "白云歌 BaiYunGe"
#define MyAppVersion "2.0.3"
#define MyAppExeName "BaiYunGe.exe"
#define MyAppPublisher "BaiYun"
#define MyAppURL "https://github.com/bilibilibaiyun"

[Setup]
AppId={{7B2E4A6F-8C3D-4E91-A5B7-2B0A1F9E8C64}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppMutex=BaiYunGe_SingleInstance_2.0
DefaultDirName={autopf}\BaiYunGe
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\BaiYunGe\Assets\app.ico
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir=..\artifacts
OutputBaseFilename=白云歌_BaiYunGe_2.0.3_x64_Setup

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
    GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\*"; \
    DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
    Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM BaiYunGe.exe /F 2>nul & exit 0"; \
    Flags: runhidden; RunOnceId: "KillApp"

[Code]
var
  NewFolderButton: TNewButton;

procedure NewFolderButtonClick(Sender: TObject);
var
  BaseDir, NewDir: string;
begin
  BaseDir := WizardForm.DirEdit.Text;
  NewDir := AddBackslash(BaseDir) + '白云歌';
  if DirExists(NewDir) then
    WizardForm.DirEdit.Text := NewDir
  else if CreateDir(NewDir) then
    WizardForm.DirEdit.Text := NewDir
  else
    MsgBox('无法创建文件夹：' + NewDir, mbError, MB_OK);
end;

procedure InitializeWizard();
begin
  // 缩短目录输入框，给「新建文件夹」按钮腾出位置（放在输入框与浏览按钮之间）。
  WizardForm.DirEdit.Width := WizardForm.DirEdit.Width - ScaleX(98);

  NewFolderButton := TNewButton.Create(WizardForm);
  NewFolderButton.Parent := WizardForm.SelectDirPage;
  NewFolderButton.Left := WizardForm.DirEdit.Left + WizardForm.DirEdit.Width + ScaleX(8);
  NewFolderButton.Top := WizardForm.DirEdit.Top;
  NewFolderButton.Width := ScaleX(90);
  NewFolderButton.Height := WizardForm.DirEdit.Height;
  NewFolderButton.Caption := '新建文件夹';
  NewFolderButton.OnClick := @NewFolderButtonClick;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Exec(ExpandConstant('{cmd}'),
      '/C rmdir /S /Q "' + ExpandConstant('{userprofile}') + '\BaiYunGe" 2>nul & ' +
      'rmdir /S /Q "D:\Users\' + GetUserNameString + '\BaiYunGe" 2>nul & ' +
      'rmdir /S /Q "' + ExpandConstant('{localappdata}') + '\BaiYunGe" 2>nul & exit 0',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
