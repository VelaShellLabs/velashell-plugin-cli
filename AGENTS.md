# AGENTS.md

> 给 AI 代理与新加入者的操作约定。**动手之前先读完本文件,以及它指向的文档。**

## 一、开工前必读:velashell-docs

VelaShell 生态的**全部文档**集中在一个仓库:
**[VelaShellLabs/velashell-docs](https://github.com/VelaShellLabs/velashell-docs)**。
本仓库**不放** `docs/`、`docs-en/` —— 设计手册、开发规范与开发文档都在那边。

**在动任何代码之前**,先把下表中与你要改的部分相关的几篇读掉。跳过这一步直接改,
结果通常是两种:与既有设计冲突,或者重复实现一个已经存在的能力。

| 位置 | 内容 |
| --- | --- |
| [`zh/host/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/host) | 宿主分层架构与依赖方向、工程化重构蓝图、交互与界面规格、快捷键参考、设置项审计,以及 SFTP / FTP / Telnet / 串口 / Redis / S3 / 系统密钥链等可行性调研 |
| [`zh/plugins/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/plugins) | 插件系统设计蓝图 01–15(进程模型、IPC 协议、权限系统、UI 扩展、威胁模型、路线图)与[进度总览 STATUS](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/plugins/STATUS.md) |
| [`zh/sdk/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/sdk) | 插件契约 SDK 参考、SDK 仓库的发版流程 |
| [`zh/cli/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/cli) | `vela-plugin` 命令行手册、CLI 仓库的发版流程 |
| [`zh/templates/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/templates) | 插件开发指南、打包与发布、模板仓库的发版流程 |

英文镜像在 [`en/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/en),与 `zh/` 同构。
[仓库首页](https://github.com/VelaShellLabs/velashell-docs)有按「我想做什么」组织的快速入口表。

## 二、涉及文档的改动一律同步到 velashell-docs

**这是本文件最重要的一条。**

- 本仓库里**不新建** `docs/`、`docs-en/` 或任何成体系的文档目录。要写文档,去 velashell-docs 开 PR。
- 改了代码,而**行为、接口、配置项、命令行、构建流程或版本纪律**与现有文档对不上时,
  必须**同时**在 velashell-docs 提一个 PR 把文档改过来。两个 PR 在正文里互相引用,一起合。
  只改代码不改文档,等于让文档开始骗人 —— 而文档是别人照抄的。
- velashell-docs 的 `zh/` 与 `en/` 是**互为镜像**的两棵树,文件一一对应。改了中文就要改英文,
  反之亦然。漏一边,两棵树就开始漂。
- velashell-docs 内部的互相引用**一律走相对路径**(如 `../templates/dev-guide.md`),
  不要写回 GitHub 绝对 URL —— 文档集中到一个仓库,消掉的正是那种一改路径就断的跨仓库链接。
- **例外**:留在代码仓库里的少数几份文件不适用上述规则,因为它们服务的是「在这个仓库里写代码」
  这件事,搬走只会离使用场景更远。各仓库的例外清单见下面第三节。

## 三、本仓库:velashell-plugin-cli(命令行与构建支持包)

产出 `VelaShell.Plugin.Cli`(dotnet tool `vela-plugin`)与 `VelaShell.PluginSdk.Build`
(插件工程**只需引用这一个包**)。两者**始终同版本发**:`.Build` 把 `vela-plugin` 的构建产物
收进包的 `tools/`,它的 targets 直接调那个打包器的命令面,参数改了而 targets 没跟上,
现象是插件作者构建到一半 `Exec` 失败。

### 构建与测试

```bash
dotnet build VelaShell.Plugin.Cli.slnx
dotnet test  VelaShell.Plugin.Cli.slnx -c Debug

# 端到端冒烟:拿刚打出的包当插件作者走一遍
dotnet pack src/VelaShell.Plugin.Cli/VelaShell.Plugin.Cli.csproj -c Release -o artifacts/nuget
dotnet pack src/VelaShell.PluginSdk.Build/VelaShell.PluginSdk.Build.csproj -c Release -o artifacts/nuget
pwsh scripts/Invoke-Smoke.ps1 -Feed ./artifacts/nuget -Version <版本>
```

冒烟夹具在 `tests/smoke/`:一个手写的最小插件工程,刻意带两个空的
`Directory.Build.props`/`.targets` 来切断向上查找 —— **插件工程是仓库外环境,仓库内的构建约定
一条也吃不到**,冒烟的全部价值就在这里。改打包器或 targets 后必须跑它。

### 两个跨仓库旋钮(都不由 Set-Version.ps1 管)

| 旋钮 | 在哪 | 抬它意味着 |
| --- | --- | --- |
| `VelaShell.PluginSdk` 的 `PackageReference` | 两个 csproj,**必须同版本** | 插件作者的编译目标契约变新。只改了输出格式的补丁版不该顺手带上 |
| `VelaAvaloniaVersion` | `Directory.Build.props` | 权威在 sdk 仓库,这里只是副本。漂了报 `VELA1006` |

契约 SDK 的版本刻意写成**字面量**、不抽成 MSBuild 属性:`Version="$(...)"` 会让
Dependabot 与 `dotnet add package` 认不出这条依赖。

### 发版脚本会写 velashell-docs

`scripts/Set-Version.ps1` 的版本横幅落点有两处在**另一个仓库**:`zh/cli/cli.md` 与 `en/cli/cli.md`。
脚本按 `-DocsRoot` → `$env:VELASHELL_DOCS` → 同级 `../velashell-docs` 找,找不到就跳过并警告。
**改了命令行的参数、输出或行为,必须同步改 velashell-docs 的 `cli.md`(中英各一份)。**

完整流程见 [`zh/cli/release-process.md`](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/release-process.md)。

### 留在本仓库的文档

`README.md`、`LICENSE`,以及 `src/**/README.md`。CLI 手册与发版流程都在 velashell-docs。
