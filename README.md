# VelaShell 插件工具链(CLI + 构建支持包)

[VelaShell](https://github.com/joesdu/VelaShell) 插件作者用的**工具**:命令行与 MSBuild 支持。

| 包 | 内容 |
| --- | --- |
| [`VelaShell.Plugin.Cli`](https://www.nuget.org/packages/VelaShell.Plugin.Cli) | `vela-plugin`(dotnet tool):从插件商店装/升/卸插件、校验清单、打 `.vpx`、签名/验签、挂载到本机宿主调试 |
| [`VelaShell.PluginSdk.Build`](https://www.nuget.org/packages/VelaShell.PluginSdk.Build) | 插件工程**只需引用这一个包**:MSBuild targets + 随包分发的打包器 + 契约程序集 + Avalonia 版本锁 |

## 快速上手

装别人的插件:

```bash
dotnet tool install -g VelaShell.Plugin.Cli
vela-plugin search redis                    # 找
vela-plugin install velashell.redis         # 装,然后重启 VelaShell
vela-plugin update                          # 以后升级
```

包来自[插件商店](http://market.easilynet.top),落到 `~/.velashell/plugins/<id>/` ——
与宿主「插件管理页 → 安装 .vpx…」同一个目录。装之前会核对整包摘要、容器摘要、签名与宿主
兼容性;`--source` 可以指到自建商店。完整命令见 [`docs/cli.md`](docs/cli.md)。

写自己的插件:见
[开发指南](https://github.com/VelaShellLabs/velashell-plugin-templates/blob/main/docs/dev-guide.md)。

## 为什么这两个包在同一个仓库

`VelaShell.PluginSdk.Build` 把 `vela-plugin` 的构建产物**收进包的 `tools/`**,它的 targets
直接调那个打包器的命令面(`validate` / `pack`)。命令行参数改了而 targets 没跟上,现象是
插件作者构建到一半 `Exec` 失败 —— 所以它俩是**一个发布单元**,始终同版本发。

拆库时也考虑过把 `.Build` 放进 sdk 仓库(名字看起来更像一家),但那会让依赖反向:
sdk 仓库要去引用 CLI 包,于是 SDK 发版被 CLI 卡住 —— 正是拆库要消掉的那种强一致。

## 插件生态的仓库分布

2026-08-27 起工具链按发布节奏拆成三个仓库,各有各的版本号,**不要求同步发版**:

| 仓库 | 产出 | 什么时候发 |
| --- | --- | --- |
| [`velashell-plugin-sdk`](https://github.com/VelaShellLabs/velashell-plugin-sdk) | `VelaShell.PluginSdk`、`.Testing` | 契约有增删改时 |
| **本仓库** `velashell-plugin-cli` | `VelaShell.Plugin.Cli`、`VelaShell.PluginSdk.Build` | 工具/打包/MSBuild 逻辑变化时 |
| [`velashell-plugin-templates`](https://github.com/VelaShellLabs/velashell-plugin-templates) | `VelaShell.Plugin.Templates` | 模板内容变化,或要把新建工程指到新版 Build 包时 |

依赖方向是单向的,没有环:

```
velashell-plugin-sdk                  契约,无上游
        ↓ NuGet: VelaShell.PluginSdk        ← 版本旋钮 VelaSdkDependencyVersion
velashell-plugin-cli                  ← 本仓库
        ↓ NuGet: VelaShell.PluginSdk.Build  ← 模板里那个 sdkVersion 默认值
velashell-plugin-templates
```

另外两个相关仓库:[joesdu/VelaShell](https://github.com/joesdu/VelaShell)(宿主主程序)、
[joesdu/velashell-plugins](https://github.com/VelaShellLabs/velashell-plugins)(第一方插件)。

## 两个跨仓库旋钮

都在 `Directory.Build.props`,**都不由 `Set-Version.ps1` 管**——它们是需要想清楚的独立决定:

| 旋钮 | 含义 | 抬它意味着 |
| --- | --- | --- |
| `VelaSdkDependencyVersion` | 引用哪一版契约 SDK | 插件作者的**编译目标契约**变新。发一个只改了输出格式的补丁版时不该顺手带上 |
| `VelaAvaloniaVersion` | 锁给插件工程的 Avalonia 版本 | 权威在 sdk 仓库,这里只是副本。改它必须跟着 SDK 走 |

第二个有构建期硬核对:`VelaShell.PluginSdk` 包把权威值导出成
`$(VelaSdkPinnedAvaloniaVersion)`,`VerifyAvaloniaVersionPin` 拿它跟本仓库的副本、
以及包里给插件工程的默认值三者相比,漂了就报 `VELA1006`。

## 在本仓库里开发

```bash
dotnet build VelaShell.Plugin.Cli.slnx
dotnet test  VelaShell.Plugin.Cli.slnx -c Debug

# 端到端冒烟:拿刚打出的包当插件作者走一遍
dotnet pack src/VelaShell.Plugin.Cli/VelaShell.Plugin.Cli.csproj -c Release -o artifacts/nuget
dotnet pack src/VelaShell.PluginSdk.Build/VelaShell.PluginSdk.Build.csproj -c Release -o artifacts/nuget
pwsh scripts/Invoke-Smoke.ps1 -Feed ./artifacts/nuget -Version 1.5.0
```

冒烟的夹具在 [`tests/smoke/`](tests/smoke/):一个手写的最小插件工程,与 `velaplugin-ui`
模板同形。它刻意带两个空的 `Directory.Build.props`/`.targets` 来切断向上查找 ——
**插件工程是仓库外环境,仓库内的构建约定一条也吃不到**,而这个冒烟的全部价值就在这里。

想验一版还没发布的契约 SDK:在 sdk 仓库 `dotnet pack -o <这里>/local-packages`,
再把 `VelaSdkDependencyVersion` 临时指过去(见 `nuget.config` 的注释)。

本仓库**不做强名称签名**,因此不需要 `STRONG_NAME_KEY` —— 未签名程序集可以引用
已签名的,方向是对的。

## 发版

```powershell
pwsh scripts/Set-Version.ps1 1.5.1     # 落版本号(3 处),连同功能改动合进 main
                                        # 再在 GitHub 上发 Release,标签 v1.5.1
```

完整流程见 [`docs/release-process.md`](docs/release-process.md)。

## 文档

[`docs/cli.md`](docs/cli.md)(中文)· [`docs-en/cli.md`](docs-en/cli.md)(English)。

## 许可

AGPL-3.0-only,见 [LICENSE](LICENSE)。
