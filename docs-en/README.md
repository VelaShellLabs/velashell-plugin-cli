# Documentation index

This repository holds the **toolchain** documentation only. Chinese version: [`../docs/`](../docs/).

| Document | Contents |
| --- | --- |
| [cli.md](cli.md) | **`vela-plugin` manual**: marketplace install/update/uninstall, the dev inner loop (`dev init`), `doctor`, validate/pack/sign, host launch arguments |
| [../docs/release-process.md](../docs/release-process.md) | **How this repository releases** (Chinese only) |

## What is not here

After the 2026-08-27 split, each document lives next to the package it describes:

| Document | Where it lives now |
| --- | --- |
| **Development guide** (tutorial: writing your first plugin) | [velashell-plugin-templates / docs-en/dev-guide.md](https://github.com/VelaShellLabs/velashell-plugin-templates/blob/main/docs-en/dev-guide.md) |
| **Packaging and publishing** | [velashell-plugin-templates / docs-en/publishing.md](https://github.com/VelaShellLabs/velashell-plugin-templates/blob/main/docs-en/publishing.md) |
| **SDK reference** | [velashell-plugin-sdk / docs-en/sdk-reference.md](https://github.com/VelaShellLabs/velashell-plugin-sdk/blob/main/docs-en/sdk-reference.md) |

Each carries the **version banner of its own package**, which is why it has to live next to that
package: keeping them here would mean every SDK release needs a commit in this repository — the
coupling the split was meant to remove.

The plugin system's **architecture documents** stay in the host repository:
<https://github.com/joesdu/VelaShell/tree/main/docs/plugins>
