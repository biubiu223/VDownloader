<#
.SYNOPSIS
  一键构建发布文件，全部输出到 dist\：
    dist\VDownloader\                              免安装程序目录（含全部依赖）
    dist\VDownloader-<版本>-win-x64-portable.zip   绿色版压缩包
    dist\VDownloader-<版本>-Setup-x64.exe          安装包（Inno Setup）
    dist\SHA256SUMS.txt                            校验文件

.EXAMPLE
  .\scripts\build-release.ps1                    # 版本号取 VDownloader.csproj 里的 <Version>
  .\scripts\build-release.ps1 -Version 1.0.1     # 指定版本号
  .\scripts\build-release.ps1 -FetchTools        # tools\ 缺组件时自动下载
#>
param(
    [string]$Version,
    [switch]$FetchTools,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 和 PowerShell 7 里 ZipFile 所在的程序集名字不同
if ($PSVersionTable.PSEdition -eq 'Core') { Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile }
else { Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem }

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'VDownloader\VDownloader.csproj'
$dist = Join-Path $root 'dist'
$pub  = Join-Path $dist 'VDownloader'

function Step([string]$msg) { Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }

# ---------------------------------------------------------------- 版本号
if (-not $Version) {
    $Version = ([xml](Get-Content $proj -Raw -Encoding UTF8)).Project.PropertyGroup |
        ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版本号格式应为 x.y.z，当前为：$Version" }
Step "构建 万能视频下载器 v$Version"

# ---------------------------------------------------------------- 组件
$needed = 'yt-dlp.exe', 'ffmpeg.exe', 'deno.exe'
$missing = $needed | Where-Object { -not (Test-Path (Join-Path $root "tools\$_")) }
if ($missing) {
    if (-not $FetchTools) { throw "tools\ 缺少组件：$($missing -join ', ')。请先运行 scripts\fetch-tools.ps1，或加上 -FetchTools 参数。" }
    Step '下载缺少的组件'
    & (Join-Path $PSScriptRoot 'fetch-tools.ps1')
}

# ---------------------------------------------------------------- dotnet
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
$userSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$hasSdk = $dotnet -and ((& $dotnet --list-sdks) -match '^8\.')
if (-not $hasSdk -and (Test-Path $userSdk)) {
    $dotnet = $userSdk
    $env:DOTNET_ROOT = Split-Path $userSdk
}
if (-not $dotnet) { throw '没有找到 .NET SDK，请先安装 .NET 8 SDK。' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# ---------------------------------------------------------------- 编译发布
Step 'dotnet publish'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
& $dotnet publish $proj -c Release -p:Platform=x64 "-p:Version=$Version" -o $pub -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

foreach ($f in 'VDownloader.exe', 'VDownloader.pri', 'MainWindow.xbf', 'tools\yt-dlp.exe', 'tools\ffmpeg.exe', 'tools\deno.exe') {
    if (-not (Test-Path (Join-Path $pub $f))) { throw "发布目录里缺少 $f" }
}
$size = (Get-ChildItem $pub -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("发布目录：{0}（{1:N0} MB）" -f $pub, $size)

# ---------------------------------------------------------------- 绿色版 zip
Step '打包绿色版 zip'
$zip = Join-Path $dist "VDownloader-$Version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# 手动写入条目，保证路径分隔符是 /（Windows PowerShell 5.1 自带的方法会写成 \）
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $pub -Recurse -File) {
        $rel = 'VDownloader/' + $file.FullName.Substring($pub.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $rel, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
Write-Host ("{0}（{1:N0} MB）" -f $zip, ((Get-Item $zip).Length / 1MB))

# ---------------------------------------------------------------- 安装包
if (-not $SkipInstaller) {
    Step '制作安装包（Inno Setup）'
    $iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw '没有找到 Inno Setup 6（ISCC.exe）。请安装 Inno Setup 6，或加上 -SkipInstaller 参数。' }
    Get-ChildItem $dist -Filter "VDownloader-*-Setup-x64.exe" | Remove-Item -Force
    & $iscc /Qp "/DMyAppVersion=$Version" "/DSourceDir=$pub" "/DOutputDir=$dist" (Join-Path $root 'installer\VDownloader.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 编译失败' }
    $setup = Join-Path $dist "VDownloader-$Version-Setup-x64.exe"
    Write-Host ("{0}（{1:N0} MB）" -f $setup, ((Get-Item $setup).Length / 1MB))
}

# ---------------------------------------------------------------- 校验文件
Step '生成 SHA256SUMS.txt'
$sums = Get-ChildItem $dist -File | Where-Object { $_.Name -match "^VDownloader-$([regex]::Escape($Version))-.*\.(zip|exe)$" } |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$sums)
$sums | ForEach-Object { Write-Host "  $_" }

Step '完成'
