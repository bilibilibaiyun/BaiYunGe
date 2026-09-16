; BaiYunGe 白云歌 安装程序脚本 (Inno Setup 7)
; 由 007 生成 — 白云歌 v2.0.3

#define MyAppName "白云歌 BaiYunGe"
#define MyAppVersion "3.0.2"
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
; 覆盖安装时若程序仍在运行（AppMutex 被占用），静默模式自动关闭它而非失败。
CloseApplications=yes
RestartApplications=no
OutputDir=..\artifacts
OutputBaseFilename=白云歌_BaiYunGe_3.0.2_x64_Setup

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
    Flags: nowait postinstall

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

function PosEx(const SubStr, S: string; Offset: Integer): Integer;
var
  Tmp: string;
  Found: Integer;
begin
  if Offset <= 1 then
    Result := Pos(SubStr, S)
  else
  begin
    Tmp := Copy(S, Offset, Length(S) - Offset + 1);
    Found := Pos(SubStr, Tmp);
    if Found > 0 then
      Result := Found + Offset - 1
    else
      Result := 0;
  end;
end;

function ExtractModelDirectory(const DataDir: string): string;
var
  ConfigPath, Value: string;
  Json: AnsiString;
  JsonStr: string;
  P, Q, R: Integer;
begin
  Result := '';
  ConfigPath := DataDir + '\config.json';
  if not FileExists(ConfigPath) then Exit;
  if not LoadStringFromFile(ConfigPath, Json) then Exit;
  JsonStr := Json;
  P := Pos('"ModelDirectory"', JsonStr);
  if P = 0 then Exit;
  Q := PosEx('"', JsonStr, P + 16);
  if Q = 0 then Exit;
  R := PosEx('"', JsonStr, Q + 1);
  if R = 0 then Exit;
  Value := Copy(JsonStr, Q + 1, R - Q - 1);
  StringChange(Value, '\\', '\');
  Result := Value;
end;

function FindDataDirectory: string;
var
  Candidate: string;
begin
  Result := '';

  Candidate := GetEnv('USERPROFILE') + '\BaiYunGe';
  if FileExists(Candidate + '\config.json') then
  begin
    Result := Candidate;
    Exit;
  end;

  Candidate := 'D:\Users\' + GetUserNameString + '\BaiYunGe';
  if FileExists(Candidate + '\config.json') then
  begin
    Result := Candidate;
    Exit;
  end;

  Candidate := ExpandConstant('{localappdata}') + '\BaiYunGe';
  if FileExists(Candidate + '\config.json') then
  begin
    Result := Candidate;
    Exit;
  end;

  Candidate := ExpandConstant('{app}') + '\.data';
  if FileExists(Candidate + '\config.json') then
  begin
    Result := Candidate;
    Exit;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  DataDir, ModelDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 删除开机自启动注册表键（清除一切痕迹）。
    Exec(ExpandConstant('{cmd}'),
      '/C reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v BaiYunGe /f 2>nul & exit 0',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    DataDir := FindDataDirectory;
    ModelDir := ExtractModelDirectory(DataDir);

    // 先删除模型目录（config.json 里配置的 ModelDirectory，可能独立于数据目录之外）。
    if (ModelDir <> '') and (ModelDir <> DataDir) and DirExists(ModelDir) then
      Exec(ExpandConstant('{cmd}'),
        '/C rmdir /S /Q "' + ModelDir + '" 2>nul & exit 0',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 再删除数据目录（userprofile / D:\Users / localappdata / appdir 四处回退）。
    Exec(ExpandConstant('{cmd}'),
      '/C rmdir /S /Q "' + GetEnv('USERPROFILE') + '\BaiYunGe" 2>nul & ' +
      'rmdir /S /Q "D:\Users\' + GetUserNameString + '\BaiYunGe" 2>nul & ' +
      'rmdir /S /Q "' + ExpandConstant('{localappdata}') + '\BaiYunGe" 2>nul & ' +
      'rmdir /S /Q "' + ExpandConstant('{app}') + '\.data" 2>nul & exit 0',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
