; BaiYunGe 白云歌 安装程序脚本 (Inno Setup 7)
; 由 007 生成 — 白云歌 v2.0.0

#define MyAppName "白云歌 BaiYunGe"
#define MyAppVersion "2.0.0"
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
OutputBaseFilename=白云歌_BaiYunGe_2.0.0_x64_Setup

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

[UninstallDelete]
; 不删除用户数据（模型/配置/日志在用户目录，不在安装目录）
