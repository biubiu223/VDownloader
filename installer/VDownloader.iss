; =====================================================================
;  万能视频下载器 (VDownloader) —— Inno Setup 安装脚本
;  一般不需要手动编译：运行 scripts\build-release.ps1 会自动调用。
;  手动编译：ISCC.exe /DMyAppVersion=1.0.0 installer\VDownloader.iss
; =====================================================================

#define MyAppName      "万能视频下载器"
#define MyAppNameEn    "VDownloader"
#define MyAppPublisher "biubiu223"
#define MyAppURL       "https://github.com/biubiu223/VDownloader"
#define MyAppExeName   "VDownloader.exe"

; 发布目录（dotnet publish 的输出），可用 /DSourceDir=... 覆盖
#ifndef SourceDir
  #define SourceDir "..\dist\VDownloader"
#endif

; 版本号：优先用命令行传入的 /DMyAppVersion，否则读取 exe 的文件版本
#ifndef MyAppVersion
  #define MyAppVersion GetVersionNumbersString(AddBackslash(SourceDir) + MyAppExeName)
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; AppId 用于识别同一个软件的不同版本，以后升级时千万不要修改
AppId={{DAE874E6-01C5-4EA4-B7C8-79770B04E5C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}

; 默认只为当前用户安装（不需要管理员权限，yt-dlp 也能原地自我更新）；
; 安装时可以在对话框里改成“为所有用户安装”
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

; 只支持 64 位 Windows 10 1809 及以上
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename={#MyAppNameEn}-{#MyAppVersion}-Setup-x64
SetupIconFile=..\VDownloader\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
ShowLanguageDialog=auto

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; 升级 / 卸载时自动关闭正在运行的程序
CloseApplications=yes
RestartApplications=no

VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimp.DeleteUserData=是否同时删除软件的设置、日志、在线下载的组件和 B站登录信息？%n%n（已下载的视频不会被删除）
english.DeleteUserData=Also delete settings, logs, downloaded components and the saved Bilibili login?%n%n(Your downloaded videos will NOT be deleted.)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "B站 / YouTube / 1800+ 网站视频下载器"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; yt-dlp -U 自我更新时可能留下的临时文件
Type: filesandordirs; Name: "{app}\tools"

[Code]
// 卸载完成后询问是否删除用户数据（%LOCALAPPDATA%\VDownloader）。
// 静默卸载时默认保留，避免误删。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\VDownloader');
    if DirExists(DataDir) and (not UninstallSilent) then
      if MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
