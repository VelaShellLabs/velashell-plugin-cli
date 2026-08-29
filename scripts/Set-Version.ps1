#Requires -Version 7.0
<#
.SYNOPSIS
    把工具链版本号写进本仓库里所有需要它的地方。

.DESCRIPTION
    本仓库产出两个**同版本发布**的包(理由见 Directory.Build.props 的注释):

      VelaShell.Plugin.Cli          dotnet tool `vela-plugin`
      VelaShell.PluginSdk.Build     插件工程引用的那一个包

    落点三处:

      Directory.Build.props            <VelaToolsVersion>        —— 两个包的版本
      zh/cli/cli.md                    版本横幅       ┐ 这两处在 velashell-docs 仓库,
      en/cli/cli.md                    version banner ┘ 是**可选**落点,见 -DocsRoot

    docs 那两处不影响功能,但它们是给人照抄的,过期版本号会被原样粘进别人的工程 ——
    2026-08-30 全部文档搬到 VelaShellLabs/velashell-docs 之后,它们不在本仓库的
    checkout 里,所以找不到就跳过。

    **注意本脚本不碰 VelaShell.PluginSdk 的引用版本。** 那是"本仓库引用哪一版契约 SDK",
    与"本仓库自己发什么版本"是两件事 —— 拆库(2026-08-27)之后正是要让它们分开:
    发一个只改了 vela-plugin 输出格式的补丁版,不该顺手把插件作者的编译目标契约也换掉。
    要抬契约版本就直接改两个 csproj 里的 PackageReference(或合掉 Dependabot 的 PR),
    那是一次独立的、需要想清楚的决定。

    **不在本仓库的落点**(各自由所在仓库的同名脚本管):
      · VelaPluginApi.SdkVersion / apiLevel 纪律 ……… velashell-plugin-sdk
      · dotnet new 模板的 sdkVersion 默认值 ………… velashell-plugin-templates
      · velashell-docs 里 zh|en/templates/dev-guide.md 的 PackageReference 片段
                                                    … velashell-plugin-templates

    ⚠️ 有一条跨仓库的**手工**后续动作:本仓库发了新版 VelaShell.PluginSdk.Build 之后,
       若希望 `dotnet new velaplugin` 生成的工程指向新版,要去 templates 仓库把
       VelaBuildPackageVersion 抬上来再发一版模板。不做也不会坏 —— 新建的工程只是
       继续引用上一版 .Build 包,那是完全可用的。

    发版流水线在解析出 Release 标签之后**第一件事**也会跑本脚本
    (见 .github/workflows/release.yml),因此产物永远与标签一致。它只改 runner 上的
    工作区,**不回写仓库**。忘了在本地跑的兜底是 CI 的 -Check 体检。

.PARAMETER Version
    目标版本,SemVer(1.5.1 或 1.6.0-preview.1)。

.PARAMETER DocsRoot
    velashell-docs 仓库的位置,版本横幅写在那里。默认先看 $env:VELASHELL_DOCS,
    再看与本仓库同级的 ../velashell-docs。找不到就跳过文档落点并提醒一句 —— 那是
    另一个仓库,CI 的 checkout 里本来就没有它,不该因此让发版流水线变红。

.PARAMETER Check
    只报告不落盘;有任何一处不同步就以退出码 1 结束。CI 用它做"仓库是否已同步"的体检。

.EXAMPLE
    pwsh scripts/Set-Version.ps1 1.5.1

.EXAMPLE
    pwsh scripts/Set-Version.ps1 1.5.1 -Check
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)] [string] $Version,
    [string] $DocsRoot,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') {
    throw "'$Version' 不是合法 SemVer。用 1.5.1 或 1.6.0-preview.1 这种形式。"
}

$root = Split-Path -Parent $PSScriptRoot

# ── velashell-docs 的位置 ────────────────────────────────────────────────────
# 2026-08-30 起全部文档搬到 VelaShellLabs/velashell-docs,版本横幅跟着走了。那是另一个
# 仓库,发版 runner 的 checkout 里没有它 —— 所以文档落点是**可选**的:本地开发时两个仓库
# 通常并排放着,找得到就一起改;找不到就在末尾提醒一句,不让流水线因为缺个兄弟仓库而红。
if (-not $DocsRoot) {
    $DocsRoot = if ($env:VELASHELL_DOCS) { $env:VELASHELL_DOCS }
                else { Join-Path (Split-Path -Parent $root) "velashell-docs" }
}
$docsAvailable = Test-Path (Join-Path $DocsRoot "zh")
$skippedDocs = [System.Collections.Generic.List[string]]::new()

# ── 落点清单 ────────────────────────────────────────────────────────────────
# 每条都用**锚定到上下文**的模式,不做"全局替换旧版本号"。后者会误伤示例输出里那些
# 只是碰巧等于当前版本的数字(cli.md 里 `doctor` 的示例输出就有好几个别的版本号:
# 宿主版本、SDK 版本、Avalonia 版本,一个都不该跟着动)。
$edits = [System.Collections.Generic.List[hashtable]]::new()

$edits.Add(@{
    Path    = 'Directory.Build.props'
    Pattern = '(?<pre><VelaToolsVersion Condition="[^"]*">)(?<val>[^<]+)(?<post></VelaToolsVersion>)'
    What    = 'VelaToolsVersion'
})
$edits.Add(@{
    Repo    = "docs"
    Path    = "zh/cli/cli.md"
    Pattern = '(?<pre>适用版本:vela-plugin \*\*)(?<val>[^*]+)(?<post>\*\*)'
    What    = '版本横幅'
})
$edits.Add(@{
    Repo    = "docs"
    Path    = "en/cli/cli.md"
    Pattern = '(?<pre>Applies to vela-plugin \*\*)(?<val>[^*]+)(?<post>\*\*)'
    What    = 'version banner'
})

# ── 应用 ────────────────────────────────────────────────────────────────────
$changed = [System.Collections.Generic.List[object]]::new()
foreach ($edit in $edits) {
    $inDocs = $edit.ContainsKey("Repo") -and $edit.Repo -eq "docs"
    if ($inDocs -and -not $docsAvailable) { $skippedDocs.Add($edit.Path); continue }

    $path = if ($inDocs) { Join-Path $DocsRoot $edit.Path } else { Join-Path $root $edit.Path }
    if (-not (Test-Path $path)) { throw "落点文件不存在:$($edit.Path)" }

    $text = [IO.File]::ReadAllText($path)
    $found = [regex]::Matches($text, $edit.Pattern)
    if ($found.Count -eq 0) {
        # 模式失配 = 文件结构变了而本脚本没跟上。静默跳过等于把"漏改一处"重新放回来,
        # 所以这里直接断掉,让人当场看见。
        throw "在 $($edit.Path) 里没匹配到「$($edit.What)」。文件结构改过了?请同步更新 scripts/Set-Version.ps1。"
    }

    $stale = @($found | Where-Object { $_.Groups['val'].Value -cne $Version })
    if ($stale.Count -eq 0) { continue }

    $changed.Add([pscustomobject]@{
        File = if ($inDocs) { "velashell-docs/" + $edit.Path } else { $edit.Path }
        What = $edit.What
        From = (($stale | ForEach-Object { $_.Groups['val'].Value } | Select-Object -Unique) -join ', ')
        To   = $Version
    })
    if ($Check) { continue }

    $updated = [regex]::Replace($text, $edit.Pattern, {
        param($m) $m.Groups['pre'].Value + $Version + $m.Groups['post'].Value
    })
    # 保留文件原有的 BOM 状态,免得 diff 里多出一堆与版本号无关的整文件改动。
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($hasBom))
}

if ($skippedDocs.Count -gt 0) {
    Write-Warning @"
没找到 velashell-docs(试过 $DocsRoot),跳过了这几处文档里的版本横幅:
$($skippedDocs -join [Environment]::NewLine)
文档在 https://github.com/VelaShellLabs/velashell-docs —— 把它 clone 到本仓库同级目录,
或用 -DocsRoot / `$env:VELASHELL_DOCS 指过去,再跑一次即可一并更新。
"@
}

if ($changed.Count -eq 0) {
    Write-Host "版本已经是 $Version,全部落点同步,无需改动。"
    exit 0
}

$changed | Format-Table -AutoSize | Out-String | Write-Host

if ($Check) {
    Write-Host "::error::仓库里的版本号与 $Version 不同步(见上表)。跑 ``pwsh scripts/Set-Version.ps1 $Version`` 修正。"
    exit 1
}

Write-Host "已把 $($changed.Count) 处落点更新到 $Version。"

# 显式 exit 0,别靠"脚本正常结束"隐含成功。
# 调用方是 `& ./scripts/Set-Version.ps1 ...` 后面跟一句 if ($LASTEXITCODE) —— 而 .ps1
# **不调用 exit 就根本不会设置 $LASTEXITCODE**,它会原样保留调用方进程里的旧值。
# GitHub 的每个 pwsh 步骤都是全新进程,那里的旧值是 $null,于是 `$LASTEXITCODE -ne 0`
# 求值为真 —— 脚本明明改好了文件,步骤却报 exit code 1。
exit 0
