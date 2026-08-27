# 文档索引

本仓库只放**工具链**的文档。英文版见 [`../docs-en/`](../docs-en/)。

| 文档 | 内容 |
| --- | --- |
| [cli.md](cli.md) | **`vela-plugin` 手册**:从插件商店装/升/卸(`install` / `update` / `list`)、开发内环(`dev init`)、体检(`doctor`)、校验/打包/签名、宿主启动参数 |
| [release-process.md](release-process.md) | **本仓库自己怎么发版**:Release 流程、NuGet 可信发布配置、两个跨仓库旋钮 |

## 不在这里的东西

拆库之后(2026-08-27),各篇跟着自己描述的那个包走:

| 文档 | 去了哪 |
| --- | --- |
| **开发指南**(教程式,写第一个插件) | [velashell-plugin-templates / docs/dev-guide.md](https://github.com/joesdu/velashell-plugin-templates/blob/main/docs/dev-guide.md) |
| **打包与发布**(`.vpx`、签名、发到插件商店) | [velashell-plugin-templates / docs/publishing.md](https://github.com/joesdu/velashell-plugin-templates/blob/main/docs/publishing.md) |
| **SDK 参考**(契约表面、能力域一览) | [velashell-plugin-sdk / docs/sdk-reference.md](https://github.com/joesdu/velashell-plugin-sdk/blob/main/docs/sdk-reference.md) |

它们各自带着**自己那个包的版本号横幅**,所以必须跟包同仓库 —— 留在这里的话,
契约 SDK 发一版就要来改本仓库的文档,正是拆库要消掉的那种牵连。

插件系统的**架构蓝图**(进程模型、IPC 协议、权限系统、UI 扩展、威胁模型、路线图,
编号 01–15 的那批)留在主仓库:
<https://github.com/joesdu/VelaShell/tree/main/docs/plugins>
