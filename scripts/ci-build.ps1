<#
.SYNOPSIS
  GitHub Actions 专用入口：确定版本号 → 调用 build-release.ps1 -FetchTools → 生成 Release 说明。
  出错时输出 ::error 注解，不用登录也能在运行摘要页看到失败原因。

.EXAMPLE
  # 本地只预览 Release 说明（不构建）
  .\scripts\ci-build.ps1 -RefType tag -RefName v1.0.1 -NotesOnly
#>
param(
    [string]$RefType,
    [string]$RefName,
    [string]$InputVersion,
    [switch]$NotesOnly
)

# 让日志和注解里的中文正常显示
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$version = $InputVersion
if ($RefType -eq 'tag') { $version = $RefName }
Write-Host "PowerShell $($PSVersionTable.PSVersion)  ref=$RefType/$RefName  version='$version'"

# Release 说明 = CHANGELOG.md 里对应版本的段落 + .github/release-body.md（下载说明）
function Write-ReleaseNotes([string]$ver) {
    $ver = $ver.Trim().TrimStart('v', 'V')
    $changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw -Encoding UTF8
    $pattern = "(?ms)^## \[$([regex]::Escape($ver))\][^\n]*\n(.*?)(?=^## \[|^\[[^\]]+\]:|\z)"
    $m = [regex]::Match($changelog, $pattern)
    $notes = ''
    if ($m.Success) { $notes = "## 更新内容`n`n" + $m.Groups[1].Value.Trim() + "`n`n" }
    else { Write-Host "::warning::CHANGELOG.md 里没有找到 [$ver] 的段落，Release 说明只包含下载说明" }
    $notes += (Get-Content (Join-Path $root '.github\release-body.md') -Raw -Encoding UTF8)
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $out = Join-Path $dist 'RELEASE_NOTES.md'
    [IO.File]::WriteAllText($out, $notes, (New-Object Text.UTF8Encoding $false))
    Write-Host "Release 说明已生成：$out"
    return $out
}

try {
    if (-not $NotesOnly) {
        & (Join-Path $PSScriptRoot 'build-release.ps1') -Version $version -FetchTools
    }
    if ($version) { [void](Write-ReleaseNotes $version) }
}
catch {
    $msg = ($_.Exception.Message + ' | ' + $_.InvocationInfo.PositionMessage) -replace "`r?`n", ' '
    Write-Host "::error title=Build failed::$msg"
    exit 1
}
