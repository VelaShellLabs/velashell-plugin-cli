#Requires -Version 7.0
<#
.SYNOPSIS
    端到端冒烟:拿刚打出来的包,站在**插件作者**的位置上走一遍。

.DESCRIPTION
    拆库(2026-08-27)之前这一步是"装模板 → dotnet new → 构建 → 出 .vpx"。模板搬去
    joesdu/velashell-plugin-templates 之后,本仓库不能再依赖那个包来验自己的包
    —— 否则模板仓库出问题会让本仓库的 CI 无端变红,而且发本仓库的包时还得先有模板包。

    所以夹具改成自带:tests/smoke/ 下是一个**手写的最小插件工程**,与 velaplugin-ui
    模板同形(命令 + 面板 + 编译期 AXAML)。本脚本把它复制到临时目录、把包版本填进去、
    从给定的本地源还原,然后:

      1. `dotnet build -t:PackVpx`  —— 覆盖 targets 的全部四件事:共享程序集不落地、
                                       Avalonia 版本核对、清单编译期校验、一步出包;
      2. 确认 bin/vpx/ 下真的有 .vpx;
      3. 用 vela-plugin info 把容器读回来(魔数、摘要、清单);
      4. 确认插件输出目录里**没有**共享程序集 —— 出现了就说明 exclude=Runtime 的链路断了。

    夹具刻意放在临时目录而不是原地构建:tests/smoke/ 里那两个空的
    Directory.Build.props/.targets 已经切断了向上查找,但复制出去更贴近真实
    (插件作者的工程不在本仓库里)。

.PARAMETER Feed
    本地 NuGet 源目录,里面应有刚打出的 VelaShell.PluginSdk.Build.<版本>.nupkg
    与 VelaShell.Plugin.Cli.<版本>.nupkg。

.PARAMETER Version
    要验的 VelaShell.PluginSdk.Build 版本号。

.PARAMETER CliDll
    用来做第 3 步读回校验的 vela-plugin。默认取本仓库的 Release 构建产物。
    (第 1 步用的是**包里自带**的那份,由 targets 自己解析 —— 那一步才是真正在验打包器。)

.PARAMETER WorkDirectory
    工作目录。默认取 RUNNER_TEMP(CI)或系统临时目录。

.EXAMPLE
    pwsh scripts/Invoke-Smoke.ps1 -Feed ./artifacts/nuget -Version 1.5.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [string] $Version,
    [string] $CliDll,
    [string] $WorkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$feedPath = (Resolve-Path $Feed).Path
$fixture = Join-Path $root 'tests/smoke'
if (-not (Test-Path $fixture)) { throw "冒烟夹具不存在:$fixture" }

if (-not $CliDll) {
    $CliDll = Join-Path $root 'src/VelaShell.Plugin.Cli/bin/Release/net11.0/VelaShell.Plugin.Cli.dll'
}
if (-not (Test-Path $CliDll)) {
    throw "找不到 vela-plugin:$CliDll。先跑一次 ``dotnet build src/VelaShell.Plugin.Cli -c Release``,或用 -CliDll 指定。"
}

if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) 'vela-plugin-smoke'
}
if (Test-Path $WorkDirectory) { Remove-Item -Recurse -Force $WorkDirectory }
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null

Write-Host "== 冒烟:VelaShell.PluginSdk.Build $Version =="
Write-Host "   源     $feedPath"
Write-Host "   工作区 $WorkDirectory"

# ── 铺夹具 ──────────────────────────────────────────────────────────────────
Copy-Item -Recurse -Force (Join-Path $fixture '*') $WorkDirectory

$csproj = Join-Path $WorkDirectory 'Smoke.csproj'
$text = [IO.File]::ReadAllText($csproj)
if ($text -notmatch 'VELA_BUILD_VERSION') {
    throw "tests/smoke/Smoke.csproj 里找不到 VELA_BUILD_VERSION 占位符;夹具改过了?"
}
[IO.File]::WriteAllText($csproj, $text.Replace('VELA_BUILD_VERSION', $Version))

# <clear /> 很关键:不清掉的话机器上已有的 nuget.org 缓存或别的源可能把**上一版**
# 同名包喂进来,冒烟就验不到刚打出的这一版了。nuget.org 仍要留着 —— Avalonia 从那来。
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feedPath" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $WorkDirectory 'nuget.config')

Push-Location $WorkDirectory
try {
    # ── 1. 构建 + 出包 ──────────────────────────────────────────────────────
    dotnet build -c Release -t:PackVpx --nologo
    if ($LASTEXITCODE -ne 0) { throw "插件工程构建失败(见上方输出)。" }

    # ── 2. 产物在不在 ───────────────────────────────────────────────────────
    $vpx = Get-ChildItem 'bin/vpx/*.vpx' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $vpx) { throw "PackVpx 没产出 .vpx。" }
    Write-Host "   产物   $($vpx.Name)"

    # ── 3. 容器读得回来吗 ───────────────────────────────────────────────────
    dotnet $CliDll info $vpx.FullName
    if ($LASTEXITCODE -ne 0) { throw "vela-plugin info 读不回刚打出的 .vpx。" }

    # ── 4. 共享程序集有没有漏进插件输出目录 ─────────────────────────────────
    # 漏了就说明 exclude=Runtime 的链路断了。这不是"包大了一点"的问题:
    # 装载器一律让这些程序集回落到宿主那份,插件目录里那些副本只会误导人,
    # 让人以为版本是自己带的那份说了算。
    $leaked = Get-ChildItem 'bin/Release/net11.0' -Filter '*.dll' |
        Where-Object { $_.Name -like 'Avalonia*' -or $_.Name -eq 'VelaShell.PluginSdk.dll' }
    if ($leaked) {
        throw "共享程序集漏进了插件输出目录:$($leaked.Name -join ', ')"
    }

    Write-Host "== 冒烟通过 =="
}
finally {
    Pop-Location
}

exit 0
