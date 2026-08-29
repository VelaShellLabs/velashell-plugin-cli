# 发版流程(CLI + 构建支持包)

> 本篇只讲**本仓库**怎么发版。契约 SDK 见
> [velashell-plugin-sdk](https://github.com/VelaShellLabs/velashell-plugin-sdk/blob/main/docs/release-process.md),
> `dotnet new` 模板见
> [velashell-plugin-templates](https://github.com/VelaShellLabs/velashell-plugin-templates/blob/main/docs/release-process.md)。

本仓库一次发布产出两个包,共用 Release 标签里的版本号:

| 包 | 内容 |
| --- | --- |
| `VelaShell.Plugin.Cli` | dotnet tool `vela-plugin` |
| `VelaShell.PluginSdk.Build` | 插件工程引用的那一个包 |

这两个**必须同版本发**:`.Build` 把 `vela-plugin` 的构建产物收进包的 `tools/`,它的
targets 直接调那个打包器的命令面(`validate` / `pack`)。命令行参数改了而 targets 没跟上,
现象是插件作者构建到一半 `Exec` 失败 —— 所以它俩是一个发布单元。

**上下游都不必跟着发。** 契约 SDK 有自己的版本号;模板包也有自己的。本仓库发 1.5.1
不代表契约动了,也不要求模板跟着发。

---

## 一、怎么发

两步:

1. 本地落版本号,连同功能改动一起合进 `main`:

   ```powershell
   pwsh scripts/Set-Version.ps1 1.5.1
   ```

2. 在 GitHub 上**发 Release**,标签填 `v1.5.1`(带不带 `v` 都行,流水线会 `TrimStart`,
   但建议统一带)。预发布勾 prerelease,标签用 `v1.6.0-preview.1`。

流水线在解析出标签之后**第一件事**也会跑一遍 `Set-Version.ps1`,只改 runner 上的工作区、
**不回写仓库** —— 于是产物版本号永远等于 Release 标签。

### 忘了在发版前落版本号怎么办

不影响这一次发布(Stamp 步骤已经兜住了),但 `main` 落后了:`main` 上的 CI
「Version consistency check」会红一次。照它给的命令本地跑一遍,补一个 PR 合掉即可。

### 手动补跑

Actions 页面 → 选 Release 工作流 → Run workflow → 填标签。推送用 `--skip-duplicate`,
对同一标签重复跑是幂等的。想只验不推,勾上 `dryRun`。

---

## 二、NuGet 可信发布(Trusted Publishing)怎么调

推送不存 API Key:工作流拿本次运行的 GitHub OIDC 令牌去 nuget.org 换一把 1 小时有效的
临时密钥。nuget.org 那边靠一条**策略**决定「哪个仓库的哪个工作流可以代表我推包」。

⚠️ **拆库之后需要三条策略,一个仓库一条** —— 策略按 (owner, repository, workflow file)
匹配,三个仓库这三项各不相同。策略的 owner 覆盖该账号名下**全部**包,所以不必按包开。

本仓库这一条填:

| 策略字段 | 值 |
| --- | --- |
| Policy name | `velashell-plugin-cli`(随意,能认出来就行) |
| Policy owner | `joes_du` |
| Repository Owner | `VelaShellLabs` |
| **Repository** | `velashell-plugin-cli` |
| **Workflow File** | `release.yml` —— **只填文件名**,不要写 `.github/workflows/` 前缀 |
| Environment | 留空(工作流没用 GitHub Environments) |

建法:登录 nuget.org → 右上角用户名 → **Trusted Publishing** → **Add**。

### ⚠️ 新策略有 7 天窗口

私有仓库上新建的策略是「**临时激活**」状态,7 天内必须成功发布一次,否则自动失效
(可以随时重开窗口)。原因是 nuget.org 要在第一次成功发布时把 GitHub 的 repository ID
与 owner ID 记进策略,把它钉死在那个仓库上(防「删库重建同名仓库」的复活攻击)——
没有一次真实发布就拿不到那两个 ID。所以**建好策略就尽快发一次**,哪怕是 preview 版。

### 换不到密钥时先看这三样

`NuGet login` 那一步失败,九成是策略对不上:

* Repository 还写着 `velashell-plugin-toolchain`;
* Workflow File 写成了 `.github/workflows/release.yml`;
* `NUGET_USER` 填成了邮箱 —— 要的是 nuget.org 的**用户名**(profile name)。
  工作流默认取 `vars.NUGET_USER`,没配则回落到 `joes_du`。

另外 job 上的 `permissions: id-token: write` 不能少,否则 GitHub 根本不签发 OIDC 令牌。

---

## 三、本仓库不需要任何机密

程序集**不做强名称签名**。方向是对的:未签名程序集可以引用已签名的
(`vela-plugin` → `VelaShell.PluginSdk`),反过来才不行。契约 SDK 之所以必须签,
是因为**宿主**(已签名)要引用它 —— 那是 sdk 仓库的事。

推送走 OIDC。于是 fork PR 与主分支跑的是完全同一条 CI 路径,没有「拿不到密钥就降级」
的分支要维护。

---

## 四、两个跨仓库旋钮:`Set-Version.ps1` 刻意不碰

它们不是「版本号」,是**依赖决定**:

### `VelaShell.PluginSdk` 的 `PackageReference` —— 引用哪一版契约 SDK

落点是 `src/VelaShell.Plugin.Cli` 与 `src/VelaShell.PluginSdk.Build` 两个 csproj,
**两处必须同版本**。刻意写字面量而不抽成 `Directory.Build.props` 里的 MSBuild 属性:
`Version="$(...)"` 会让 Dependabot 与 `dotnet add package` 认不出这条依赖,而这个包
正是要靠它们来更新的 —— 合掉 Dependabot 的 PR 就是常规抬法,它会把两处一起抬。

抬它 = 让插件作者的**编译目标契约**变新。发一个只改了 `vela-plugin` 输出格式的补丁版时,
不该顺手把这个也换掉 —— 所以它是一次独立的、需要想清楚的决定。

抬完记得:

1. 构建一次,让 `VerifyAvaloniaVersionPin` 核对 Avalonia 版本锁是否也要跟着动;
2. 发一版本仓库的包,插件作者才吃得到。

不抬也完全正常 —— 老契约照样能打包、能校验。

### `VelaAvaloniaVersion` —— 锁给插件工程的 Avalonia 版本

**权威在 sdk 仓库,这里只是一份必须与之相等的副本。** 为什么要抄一份而不直接用权威值:
权威值来自 NuGet 还原之后生成的 `obj/*.nuget.g.props`,而 Avalonia 的 `PackageReference`
版本区间是**还原本身**要读的输入 —— 冷还原时它还是空的,区间会变成 `[]` 直接失败。

所以是「写字面量 + 构建期核对」。核对覆盖三个落点:

| 落点 | 是什么 |
| --- | --- |
| `$(VelaSdkPinnedAvaloniaVersion)` | **权威**,来自 `VelaShell.PluginSdk` 包的 `buildTransitive` props |
| `$(VelaAvaloniaVersion)` | 本仓库根 `Directory.Build.props` 的副本,精确区间 `[x.y.z]` 用的就是它 |
| `build/VelaShell.PluginSdk.Build.props` 里的同名默认值 | 插件工程侧那道 `VELA1001` 核对的基准 |

漂了会报:

| 错误码 | 含义 |
| --- | --- |
| `VELA1000` | 仓库内两处副本不一致 |
| `VELA1005` | 引用的 SDK 包太老,不导出权威值 —— 整条跨仓库核对链会被悄悄关掉,所以直接红 |
| `VELA1006` | 与权威值不一致(跨仓库漂移) |

---

## 五、发完之后要不要动模板仓库

看你想不想让 `dotnet new velaplugin` 生成的工程指向新版 `.Build`。

想 → 去 [velashell-plugin-templates](https://github.com/VelaShellLabs/velashell-plugin-templates)
抬 `VelaBuildPackageVersion` 再发一版模板。

不想 → 什么都不用做。新建的工程只是继续引用上一版 `.Build` 包,那是完全可用的 ——
这正是拆库之后的正常状态,不是遗漏。

---

## 六、端到端冒烟

发布前会跑 `scripts/Invoke-Smoke.ps1`:拿刚打出的包,把 `tests/smoke/` 下那个手写的
最小插件工程复制到临时目录、从本地源还原、构建、`-t:PackVpx`、读回容器、查共享程序集
有没有漏进插件输出目录。

**这一步不是形式。** 插件工程是仓库外环境,仓库内的 `Directory.Build.props` 一条也吃不到
(夹具里那两个空的 `Directory.Build.props`/`.targets` 就是为了切断向上查找)。
历史上正是它先发现了两个坑:

* SDK 带 `RequiresPreviewFeatures`,插件工程不开预览开关就全线 `CA2252`;
* NuGet 默认不传递 `build` 资产,Avalonia 的 AXAML 编译器到不了插件工程 —— 现象是
  运行时找不到控件,而不是构建报错。

两个坑都只有在「什么都不继承」的前提下才会显形。

> 拆库之前这一步用的是 `dotnet new velaplugin-ui`。模板搬走之后不能再那样做:
> 那会让模板仓库出问题时本仓库的 CI 无端变红,而且发本仓库的包还得先有模板包。
> 模板本身的端到端验证由模板仓库自己的 CI 负责。
