# VelaShell.Plugin.Cli(`vela-plugin`)

VelaShell 插件开发者的命令行工具,以 .NET 全局工具形式分发。

```bash
dotnet tool install -g VelaShell.Plugin.Cli
vela-plugin --help
```

## 从插件商店装插件

```bash
vela-plugin search redis                 # 找
vela-plugin install velashell.redis      # 装(商店上最新的正式版)
vela-plugin list                         # 装了啥
vela-plugin update                       # 都升到最新
```

包落到 `~/.velashell/plugins/<id>/`(与宿主插件管理页同一个目录),重启 VelaShell 生效。
装之前会核对整包摘要、容器摘要、签名与宿主兼容性;未签名的包在非交互环境下一律拒装,
要装得显式给 `--allow-unsigned`。`--source` / `VELA_PLUGIN_MARKET` 可以指到自建商店。

## 开发内环:三步到断点

```bash
dotnet build
vela-plugin dev init      # 生成 IDE 启动配置(读 ~/.velashell/host.json 找到本机安装)
# 在 IDE 里按 F5
```

`dev init` 生成的启动配置会以调试器附着的方式启动**本机已安装的 VelaShell**,
用 `--dev-root` 挂载本工程输出、用 `--data-root` 起一个独立数据根的调试实例
(于是日常那份 VelaShell 可以一直开着),隔离插件再加 `--wait-debugger`。

改完代码 `dotnet build`,在插件管理页点"重新加载"即可跑上新代码,不必重启宿主;
`dev init --watch` 则重编后自动重载。

## 命令

| 命令 | 作用 |
| --- | --- |
| `vela-plugin install <id>[@<版本>]` | 从插件商店装(`--version` / `--pre` / `--source` / `--prefix` / `--trust` / `--allow-unsigned` / `--force` / `--no-cache` / `--download-only`) |
| `vela-plugin install <pkg.vpx>` | 装一个本地包,选项同上 |
| `vela-plugin uninstall <id>` | 删掉已安装插件的目录(宿主库里的插件数据不动) |
| `vela-plugin update [<id>]` | 升到商店上的新版(`--check` 只报告) |
| `vela-plugin list` | 列出已装插件:版本、来源、发布者指纹 |
| `vela-plugin search [词]` | 搜商店 |
| `vela-plugin dev init [dir]` | 生成 IDE 启动配置(`--host` / `--exe` / `--data-root` / `--shared-data` / `--watch` / `--profile` / `--link`) |
| `vela-plugin dev run [dir]` | 不开 IDE,直接用同样的参数拉起宿主(`--wait` 等它退出) |
| `vela-plugin dev list` / `dev prune` | 查看 / 清理全局登记的开发根 |
| `vela-plugin dev link [dir]` / `dev unlink [dir]` | 把输出目录常挂进宿主(旧名 `dev-link` / `dev-unlink`) |
| `vela-plugin hosts` | 列出本机登记过的 VelaShell 安装 |
| `vela-plugin doctor [dir]` | 体检:宿主、清单兼容闸、构建产物、启动配置(有问题退出码 1) |
| `vela-plugin validate [dir]` | 校验 `plugin.json` 与入口程序集(与宿主同一套规则) |
| `vela-plugin pack <dir>` | 把插件产物目录打成 `.vpx` 包(可同时签名) |
| `vela-plugin info <pkg.vpx>` / `info <id>` | 查看容器头、签名状态与清单;参数是 id 时查商店那条 |
| `vela-plugin verify <pkg.vpx>` | 校验载荷摘要与签名 |
| `vela-plugin unpack <pkg.vpx> [dir]` | 解包(排障用) |
| `vela-plugin keygen` | 生成 P-256 签名密钥对 |
| `vela-plugin sign <pkg.vpx> -k key.pem` | 给已有包补签名 |

> 命令行装包与宿主插件管理页装包落到同一个目录,差别只有一处:管理页会另写一份**受保护的
> 安装收据**做事后防篡改(密钥在宿主进程里,CLI 造不出来);作为交换,能在装之前做完的检查
> 命令行一条不少。要那层事后保护就走管理页。

打包不必装这个工具:`VelaShell.PluginSdk.Build` 包内已带同一份可执行体,
插件工程 `dotnet build -t:PackVpx` 直接出包。装全局工具是为了开发内环、体检、
签名与包检查。

- 命令行手册:<https://github.com/joesdu/velashell-plugin-cli/blob/main/docs/cli.md>
- 插件开发指南:<https://github.com/joesdu/velashell-plugin-templates/blob/main/docs/dev-guide.md>
- 打包与发布:<https://github.com/joesdu/velashell-plugin-templates/blob/main/docs/publishing.md>
- 插件商店:<http://market.easilynet.top>
