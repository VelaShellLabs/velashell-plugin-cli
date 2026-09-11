# VelaShell 插件命令行工具

[VelaShell](https://github.com/joesdu/VelaShell) 插件作者用的**命令行工具**。

| 包 | 内容 |
| --- | --- |
| [`VelaShell.Plugin.Cli`](https://www.nuget.org/packages/VelaShell.Plugin.Cli) | `vela-plugin`(dotnet tool):从插件商店装/升/卸插件、校验清单、打 `.vpx`、签名/验签、挂载到本机宿主调试 |

> 写插件要引用的那个包是 [`VelaShell.PluginSdk.Build`](https://www.nuget.org/packages/VelaShell.PluginSdk.Build),
> 它在 [`velashell-plugin-sdk`](https://github.com/VelaShellLabs/velashell-plugin-sdk)
> (2026-09-11 从本仓库搬过去,与契约包同版本发布)。那个包**自带打包器**,所以插件工程
> `dotnet build -t:PackVpx` 不必安装任何全局工具 —— 也就是说,它与本仓库无关。

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
兼容性;`--source` 可以指到自建商店。完整命令见 [`vela-plugin` 手册](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/cli.md)。

写自己的插件:见
[开发指南](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/dev-guide.md)。

## `.Build` 为什么搬走了

`VelaShell.PluginSdk.Build` 决定的是**插件作者编译时看到的那份契约**,所以它属于契约仓库:
放在那里它用 `ProjectReference` 引契约、与契约同版本同一次发布,「.Build 引用的是哪一版
SDK」这个旋钮连同它漂移的可能一并消失。2026-09-11 搬走的就是这个理由。

打包器跟着一起去了那边(`VelaShell.PluginSdk.Packer`,只有 `validate` / `pack` / `info`
三条命令)。这不是重复造轮子:`.vpx` 的定义 —— `VpxContainer` 与 `PluginManifestReader` ——
本来就在 `VelaShell.PluginSdk` 里,`vela-plugin` 也只是它的一个调用方。让 `.Build` 绕一趟
本仓库,换来的只是一个要人盯着的跨仓库版本旋钮。

**于是本仓库没有下游。** `vela-plugin` 是面向人的完整工具(商店、开发内环、签名、体检),
发不发版与插件作者能不能出包毫无关系;两边都过同一个 `VpxContainer`,包格式一致由类型保证。

## 插件生态的仓库分布

2026-08-27 起工具链按发布节奏拆成三个仓库,各有各的版本号,**不要求同步发版**:

| 仓库 | 产出 | 什么时候发 |
| --- | --- | --- |
| [`velashell-plugin-sdk`](https://github.com/VelaShellLabs/velashell-plugin-sdk) | `VelaShell.PluginSdk`、`.Testing`、`.Build` | 契约有增删改时,或 MSBuild/打包逻辑变化时 |
| **本仓库** `velashell-plugin-cli` | `VelaShell.Plugin.Cli` | 命令行工具本身变化时 |
| [`velashell-plugin-templates`](https://github.com/VelaShellLabs/velashell-plugin-templates) | `VelaShell.Plugin.Templates` | 模板内容变化,或要把新建工程指到新版 Build 包时 |

每条依赖都是一条 NuGet **已发布包**的引用,版本写死:

依赖方向是单向的,没有环:

```
velashell-plugin-sdk                  契约 + .Build + 打包器,无上游
        ↓ NuGet: VelaShell.PluginSdk        ← 版本写在 src/VelaShell.Plugin.Cli 的 csproj 上
velashell-plugin-cli                  ← 本仓库,vela-plugin
                                        (没有下游 —— 谁都不引用它)

velashell-plugin-sdk
        ↓ NuGet: VelaShell.PluginSdk.Build  ← 模板生成的工程里那一行
velashell-plugin-templates
```

另外三个相关仓库:[joesdu/VelaShell](https://github.com/joesdu/VelaShell)(宿主主程序)、
[VelaShellLabs/velashell-plugins](https://github.com/VelaShellLabs/velashell-plugins)(第一方插件)、
[VelaShellLabs/velashell-docs](https://github.com/VelaShellLabs/velashell-docs)(**全部文档**,
2026-08-30 起各仓库的 `docs/` 都搬到了那里)。

## 唯一的跨仓库旋钮

`src/VelaShell.Plugin.Cli` 里 `VelaShell.PluginSdk` 的 `PackageReference` ——
**不由 `Set-Version.ps1` 管**,它是一次需要想清楚的独立决定:抬它 = 打包器自己拿哪一版
契约去读 `plugin.json` 与 `.vpx` 容器。发一个只改了输出格式的补丁版时不该顺手带上。

版本刻意写成**字面量**、不抽成 MSBuild 属性:`Version="$(...)"` 会让 Dependabot 与
`dotnet add package` 认不出这条依赖,而这个包正是要靠它们来更新的。

> 插件作者的**编译目标**契约不在这里 —— 那由 sdk 仓库的 `VelaShell.PluginSdk.Build`
> 决定。Avalonia 版本锁同理:副本与它的构建期核对(`VELA1000` / `VELA1006`)都随 `.Build`
> 搬去了 sdk 仓库,权威值本来就在那里。

## 在本仓库里开发

```bash
dotnet build VelaShell.Plugin.Cli.slnx
dotnet test  VelaShell.Plugin.Cli.slnx -c Debug
dotnet pack  src/VelaShell.Plugin.Cli/VelaShell.Plugin.Cli.csproj -c Release -o artifacts/nuget
```

插件工程的端到端冒烟(最小插件工程 → 构建 → 出 `.vpx`)随 `VelaShell.PluginSdk.Build`
搬去了 sdk 仓库(`scripts/Invoke-Smoke.ps1` + `tests/smoke/`),那边自带打包器,与本仓库无关。

本仓库**不做强名称签名**,因此不需要 `STRONG_NAME_KEY` —— 未签名程序集可以引用
已签名的,方向是对的。

## 发版

```powershell
pwsh scripts/Set-Version.ps1 1.5.1     # 落版本号(3 处),连同功能改动合进 main
                                        # 再在 GitHub 上发 Release,标签 v1.5.1
```

完整流程见[发版流程](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/release-process.md)(在文档仓库)。

## 文档

文档不在本仓库 —— 2026-08-30 起全部集中到
**[VelaShellLabs/velashell-docs](https://github.com/VelaShellLabs/velashell-docs)**。本仓库这份在
[`zh/cli/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/cli)(中文)·
[`en/cli/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/en/cli)(English):
[`vela-plugin` 手册](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/cli.md)与[发版流程](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/release-process.md)。

隔壁还有:[开发指南](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/dev-guide.md)、
[打包发布](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/publishing.md)、
[SDK 参考](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/sdk/sdk-reference.md),
以及插件系统的[架构蓝图](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/plugins)。

> `scripts/Set-Version.ps1` 会顺带把 `zh|en/cli/cli.md` 的版本横幅一起改掉,前提是
> velashell-docs 就 clone 在本仓库同级目录(否则跳过并提醒,见其 `-DocsRoot`)。

## 许可

AGPL-3.0-only,见 [LICENSE](LICENSE)。
