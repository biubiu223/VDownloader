<#
.SYNOPSIS
  GitHub Actions 专用入口：确定版本号 → 调用 build-release.ps1 -FetchTools。
  出错时输出 ::error 注解，不用登录也能在运行摘要页看到失败原因。
#>
param(
    [string]$RefType,
    [string]$RefName,
    [string]$InputVersion
)

# 让日志和注解里的中文正常显示
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$version = $InputVersion
if ($RefType -eq 'tag') { $version = $RefName }
Write-Host "PowerShell $($PSVersionTable.PSVersion)  ref=$RefType/$RefName  version='$version'"

try {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Version $version -FetchTools
}
catch {
    $msg = ($_.Exception.Message + ' | ' + $_.InvocationInfo.PositionMessage) -replace "`r?`n", ' '
    Write-Host "::error title=Build failed::$msg"
    exit 1
}
