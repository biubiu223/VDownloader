<#
.SYNOPSIS
  下载程序依赖的外部组件到 tools\ 目录，并逐个校验 SHA-256：
    yt-dlp.exe  —— 官方 GitHub 发布版（对照 SHA2-256SUMS）
    deno.exe    —— 官方 GitHub 发布版（对照 .sha256sum）
    ffmpeg.exe  —— gyan.dev 发布版 essentials 静态编译（对照 .sha256）

.EXAMPLE
  .\scripts\fetch-tools.ps1          # 只下载缺少的组件
  .\scripts\fetch-tools.ps1 -Force   # 全部重新下载为最新版
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# Windows PowerShell 5.1 和 PowerShell 7 里 ZipFile 所在的程序集名字不同
if ($PSVersionTable.PSEdition -eq 'Core') { Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile }
else { Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem }

$root  = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null

# 下载的文件先放进一个全新的临时目录，校验通过后才移进 tools
$work = Join-Path ([IO.Path]::GetTempPath()) ("vdl-fetch-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null

function Get-File([string]$url, [string]$name) {
    $out = Join-Path $work $name
    Write-Host "  下载 $url"
    Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing -TimeoutSec 600 -Headers @{ 'User-Agent' = 'VDownloader-build' }
    return $out
}

function Assert-Sha256([string]$file, [string]$checksumText, [string]$fileName) {
    # 从校验文本里找到对应文件的 64 位十六进制哈希。兼容三种格式：
    #   sha256sum 格式（“哈希  文件名”同一行）、纯哈希、PowerShell Get-FileHash 输出（Hash 和 Path 分两行）
    $hexRe = '\b[0-9A-Fa-f]{64}\b'
    $line = ($checksumText -split "`r?`n") |
        Where-Object { $_ -match [regex]::Escape($fileName) -and $_ -match $hexRe } | Select-Object -First 1
    $source = if ($line) { $line } else { $checksumText }
    $m = [regex]::Match($source, $hexRe)
    if (-not $m.Success) { throw "校验文件里没有找到 $fileName 的 SHA-256" }
    $expected = $m.Value.ToLower()
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $expected) { throw "$fileName 校验失败！期望 $expected，实际 $actual" }
    Write-Host "  SHA-256 校验通过：$actual"
}

function Expand-One([string]$zip, [string]$entryName, [string]$dest) {
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $e = $z.Entries | Where-Object { $_.Name -ieq $entryName } | Select-Object -First 1
        if (-not $e) { throw "压缩包里没有 $entryName" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
    } finally { $z.Dispose() }
}

function Need([string]$exe) { $Force -or -not (Test-Path (Join-Path $tools $exe)) }

try {
    if (Need 'yt-dlp.exe') {
        Write-Host '[yt-dlp]'
        $base = 'https://github.com/yt-dlp/yt-dlp/releases/latest/download'
        $exe = Get-File "$base/yt-dlp.exe" 'yt-dlp.exe'
        $sums = Get-Content (Get-File "$base/SHA2-256SUMS" 'SHA2-256SUMS') -Raw
        # SHA2-256SUMS 里每行是“哈希  文件名”，要精确匹配 yt-dlp.exe 这一行
        $line = ($sums -split "`r?`n") | Where-Object { $_ -match '\s\*?yt-dlp\.exe$' } | Select-Object -First 1
        Assert-Sha256 $exe $line 'yt-dlp.exe'
        Move-Item $exe (Join-Path $tools 'yt-dlp.exe') -Force
    }

    if (Need 'deno.exe') {
        Write-Host '[deno]'
        $name = 'deno-x86_64-pc-windows-msvc.zip'
        $base = 'https://github.com/denoland/deno/releases/latest/download'
        $zip = Get-File "$base/$name" $name
        $sum = Get-Content (Get-File "$base/$name.sha256sum" "$name.sha256sum") -Raw
        Assert-Sha256 $zip $sum $name
        Expand-One $zip 'deno.exe' (Join-Path $tools 'deno.exe')
    }

    if (Need 'ffmpeg.exe') {
        Write-Host '[ffmpeg]'
        $name = 'ffmpeg-release-essentials.zip'
        $base = 'https://www.gyan.dev/ffmpeg/builds'
        $zip = Get-File "$base/$name" $name
        $sum = Get-Content (Get-File "$base/$name.sha256" "$name.sha256") -Raw
        Assert-Sha256 $zip $sum $name
        Expand-One $zip 'ffmpeg.exe' (Join-Path $tools 'ffmpeg.exe')
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '组件就绪：'
Get-ChildItem $tools -Filter *.exe | ForEach-Object { '  {0,-12} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
