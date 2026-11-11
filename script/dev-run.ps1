#Requires -Version 5.1
<#
.SYNOPSIS
    本地调试启动器：补齐开发目录缺失的图标，然后启动调试版客户端。

.DESCRIPTION
    ShiJian 的应用图标由服务端写在 <安装目录>/Data/AppIcons，网站图标在 <安装目录>/WebFavicons；
    而开发运行时数据根目录是 ShiJian.Client/bin/Debug/net10.0/，
    缺图标时界面会全部回退成默认图标。本脚本发现目标目录为空就从安装目录复制一份。

    调试版连接的是本机已在运行的 shijian-server（默认 127.0.0.1:37091），不需要另起后端；
    DEBUG 构建没有单实例锁，可以和安装版同时运行。

.PARAMETER InstallDir
    安装版目录，用于取图标。默认 "D:/Program Files/shijian"

.PARAMETER NoBuild
    跳过编译，直接运行已有产物

.EXAMPLE
    ./script/dev-run.ps1

.EXAMPLE
    ./script/dev-run.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'D:/Program Files/shijian',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$clientProject = [IO.Path]::Combine($root, 'ShiJian.Client', 'ShiJian.Client.csproj')
$outDir = [IO.Path]::Combine($root, 'ShiJian.Client', 'bin', 'Debug', 'net10.0')

function Copy-IconFolder {
    param(
        [string]$Source,
        [string]$Destination,
        [string]$Label
    )

    if (Test-Path $Destination) {
        $existing = @(Get-ChildItem -Path $Destination -Recurse -File -ErrorAction SilentlyContinue)
        if ($existing.Count -gt 0) {
            Write-Host ("[skip] {0}：已有 {1} 个文件" -f $Label, $existing.Count)
            return
        }
    }

    if (-not (Test-Path $Source)) {
        Write-Warning ("{0} 源目录不存在，跳过：{1}" -f $Label, $Source)
        return
    }

    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    if (Test-Path $Destination) {
        # 目标已存在（比如空目录）时，把源目录里的内容拷进去，避免套成 AppIcons/AppIcons
        Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
    }
    else {
        Copy-Item -Path $Source -Destination $Destination -Recurse -Force
    }
    $copied = @(Get-ChildItem -Path $Destination -Recurse -File -ErrorAction SilentlyContinue)
    Write-Host ("[copy] {0}：已从安装目录复制 {1} 个文件" -f $Label, $copied.Count)
}

Write-Host '== ShiJian 调试版 =='
Write-Host ("安装目录: {0}" -f $InstallDir)

Copy-IconFolder `
    -Source ([IO.Path]::Combine($InstallDir, 'Data', 'AppIcons')) `
    -Destination ([IO.Path]::Combine($outDir, 'Data', 'AppIcons')) `
    -Label '应用图标'

Copy-IconFolder `
    -Source ([IO.Path]::Combine($InstallDir, 'WebFavicons')) `
    -Destination ([IO.Path]::Combine($outDir, 'WebFavicons')) `
    -Label '网站图标'

Write-Host ''
Write-Host '正在启动（server: 127.0.0.1:37091，可与安装版同时运行）'
Write-Host ''

$dotnetArgs = @('run', '--project', $clientProject, '-c', 'Debug')
if ($NoBuild) { $dotnetArgs += '--no-build' }

& dotnet @dotnetArgs
