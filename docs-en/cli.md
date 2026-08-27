# `vela-plugin` CLI Manual

> Applies to vela-plugin **1.5.0** (`vela-plugin --version` tells you what you have).
> See also (other repositories): [Development Guide](https://github.com/joesdu/velashell-plugin-templates/blob/main/docs-en/dev-guide.md) · [Packaging and Publishing](https://github.com/joesdu/velashell-plugin-templates/blob/main/docs-en/publishing.md) · [SDK Reference](https://github.com/joesdu/velashell-plugin-sdk/blob/main/docs-en/sdk-reference.md)

`vela-plugin` is the plugin author's command-line tool. It calls the same implementation the
host uses (`VelaShell.PluginSdk`: manifest parsing, `.vpx` container, signature verification),
so **there is no gap where the tool accepts what the host rejects**.

```bash
dotnet tool install -g VelaShell.Plugin.Cli     # install
dotnet tool update  -g VelaShell.Plugin.Cli     # upgrade
vela-plugin --version
```

> **Packing does not need this tool.** The packer ships inside `VelaShell.PluginSdk.Build`, so
> `dotnet build -t:PackVpx` works out of the box. Install the global tool for the development
> inner loop (`dev init`), the health check (`doctor`), signing and package inspection.

---

## 0. The one-minute workflow

```bash
dotnet new install VelaShell.Plugin.Templates      # one-off
dotnet new velaplugin -n Snippets --publisher acme
cd Snippets

dotnet build
vela-plugin dev init      # writes the IDE launch profile → press F5 to debug
vela-plugin doctor        # ask this first when something is off

dotnet build -c Release -t:PackVpx                 # → bin/vpx/acme.snippets-0.1.0.vpx
vela-plugin sign bin/vpx/acme.snippets-0.1.0.vpx -k ~/keys/acme.pem
vela-plugin verify bin/vpx/acme.snippets-0.1.0.vpx
```

---

## 1. Command overview

| Command | Purpose |
| --- | --- |
| [`install`](#install) | Install by id from the [marketplace](http://market.easilynet.top), or install a local `.vpx` |
| [`uninstall`](#uninstall) | Remove an installed plugin's directory |
| [`update`](#update) | Move installed plugins up to newer marketplace versions |
| [`list`](#list) | Show what is installed, where it came from, whether it is signed |
| [`search`](#search) | Search the marketplace |
| [`dev init`](#dev-init) | Write an IDE launch profile that starts the installed VelaShell with this plugin mounted |
| [`dev run`](#dev-run) | Start the host with the same arguments, no IDE required |
| [`dev list` / `dev prune`](#dev-list--dev-prune) | Inspect / clean the globally registered development roots |
| [`dev link` / `dev unlink`](#dev-link--dev-unlink) | Mount an output directory permanently (old names: `dev-link` / `dev-unlink`) |
| [`hosts`](#hosts) | List the VelaShell installations registered on this machine |
| [`doctor`](#doctor) | Health check: host, manifest, build output, launch profile |
| [`validate`](#validate) | Validate `plugin.json` and the entry assembly |
| [`pack`](#pack) | Pack an output directory into `.vpx` |
| [`sign`](#sign) / [`verify`](#verify) | Sign / verify a package |
| [`keygen`](#keygen) | Create a P-256 signing key pair |
| [`info`](#info) / [`unpack`](#unpack) | Inspect a package header and manifest (or a marketplace listing) / extract (diagnostics) |

Conventions: exit code `0` on success, `1` on failure (readable errors go to stderr, prefixed
`error:` / `warning:`). Relative paths are accepted and echoed back as absolute. Nothing needs
elevation; apart from the project's `Properties/launchSettings.json` written by `dev init`,
only `~/.velashell` and paths you name explicitly are touched.

---

## 2. Installing from the marketplace

```bash
vela-plugin search redis                    # find
vela-plugin info velashell.redis            # inspect: author, license, every version
vela-plugin install velashell.redis         # install the newest stable version
vela-plugin list                            # what is installed
vela-plugin update                          # move everything up
```

Packages land in `~/.velashell/plugins/<id>/` - the same directory the host's
"plugin manager -> Install .vpx…" uses. **Restart VelaShell** to load a newly installed plugin.

> **The one difference from installing through the manager: no post-install tamper detection.**
> The manager records a **protected installation receipt** (content hash plus integrity
> protection); if anything later modifies the files, the host marks the plugin Invalid on
> startup and asks for a reinstall. The key and format for that receipt live inside the host
> process, so the CLI cannot produce one.
> In exchange, **every check that can be made before installing is made here**: the file digest
> against what the marketplace declares, the container's own payload digest, signature
> verification, the packaged manifest matching the requested id and version, and apiLevel /
> `minSdkVersion` / `minHostVersion` against the VelaShell installed on this machine.
> Want the post-install protection? Use the manager. Want one command? Use this.

### `install`

```bash
vela-plugin install <id>[@<version>]     # from the marketplace
vela-plugin install <package.vpx>        # from a local file
```

An argument that **looks like a path** (contains `/` or `\`, ends in `.vpx`, or names an
existing file) is treated as a local package; anything else is a marketplace id.

| Option | Meaning |
| --- | --- |
| `--version <v>` | Version to install; same as `<id>@<version>` |
| `--pre` | Consider pre-releases. **Stable only by default** - shipping a preview should not drag everyone along |
| `--source <url>` | Point at a different (self-hosted) marketplace. Environment equivalent `VELA_PLUGIN_MARKET`; the option wins |
| `--prefix <dir>` | Install root, default `~/.velashell/plugins` |
| `--trust <fingerprint>` | Require the signer's fingerprint to equal this (`SHA256:…`, case-insensitive) |
| `--allow-unsigned` | Allow a package that carries no signature |
| `--force` | Reinstall even if that exact version is already installed |
| `--no-cache` | Ignore the download cache (`~/.velashell/cache/vpx/`) |
| `--download-only [dir]` | Fetch and verify only; do not install |

**The signature policy is the host's policy:**

| Package state | What the CLI does |
| --- | --- |
| Valid signature | Installs, prints the public-key fingerprint and records it |
| Unsigned | Asks y/N on an interactive terminal; **always refuses when non-interactive** (CI, pipes) unless `--allow-unsigned` is given |
| Broken signature / modified content | **Always refused**, with no override |

When `--trust` is given it **overrules** `--allow-unsigned`: a mismatched fingerprint, or a
package with no signature at all, is refused. That is what makes `--trust` mean something in CI.

The installed directory gains a `.vela-install.json` recording the version, origin, both
digests, the publisher fingerprint and the install time. It is an ordinary file that any local
process can edit - it feeds `list` and `update`, and it is **not** a security boundary (that is
precisely the difference from the protected receipt above).

### `uninstall`

```bash
vela-plugin uninstall <id> [--prefix <dir>]
```

Removes the directory. **The plugin's data in the host's database (KV, secrets, time series) is
kept** - its encryption and database lock belong to the host, so only the manager's uninstall
can clear it. Reinstalling the same id picks the data back up.

### `update`

```bash
vela-plugin update                # every installed plugin
vela-plugin update <id>           # just one
vela-plugin update --check        # report only
```

Compares versions by id. Ids the marketplace does not carry (hand-placed, private) are skipped
rather than failed. One plugin failing does not stop the rest; the exit code reports the tally.
`--pre` / `--source` / `--prefix` / `--trust` / `--allow-unsigned` mean what they do for `install`.

### `list`

```bash
vela-plugin list [--prefix <dir>]
```

Lists the plugins under the install root: id, version, origin (marketplace host / local package
/ no install record) and publisher fingerprint. "No install record" is a directory someone
copied in by hand - the CLI has no idea where it came from.

### `search`

```bash
vela-plugin search [text] [--page N] [--size N] [--source <url>]
```

With no text it lists the first page.

> **Self-hosting a marketplace**: point `--source` or `VELA_PLUGIN_MARKET` at your own. It needs
> three read-only endpoints: `GET /api/plugins?q=&page=&size=`, `GET /api/plugins/{id}` and
> `GET /api/plugins/{id}/versions/{version}/download` (returning
> `{url, fileSha256, payloadSha256, packageSize}`). Only `http(s)` is accepted; a plain-HTTP
> download URL earns a warning - the digest is still checked, but nobody can vouch for who served it.

---

## 3. The development inner loop

### `dev init`

```bash
vela-plugin dev init [projectDir] [options]
```

Writes (or merges into) a launch profile in `Properties/launchSettings.json` that starts the
**installed VelaShell under the debugger** with this project's build output mounted.

It finds the host through `~/.velashell/host.json`, which VelaShell **writes on every launch**
(path, version, apiLevel, bundled SDK version, Avalonia version, data root).

> **Prerequisite: VelaShell must have been started at least once on this machine.**
> Otherwise point the tool at the binary with `--exe`.

```jsonc
{
  "profiles": {
    "VelaShell": {
      "commandName": "Executable",
      "executablePath": "C:\\Users\\joe\\AppData\\Local\\Programs\\VelaShell\\VelaShell.exe",
      "commandLineArgs": "--dev-root C:\\work\\Snippets\\bin\\Debug --wait-debugger acme.snippets --data-root C:\\Users\\joe\\.velashell-dev",
      "workingDirectory": "C:\\Users\\joe\\AppData\\Local\\Programs\\VelaShell"
    }
  }
}
```

| Argument | The problem it solves |
| --- | --- |
| `--dev-root <dir>` | Mounts the project output (the **parent** directory: the host scans its immediate sub-directories). Travels with the project, writes no machine-wide state |
| `--wait-debugger <id>` | An isolated plugin's process suspends **before loading the assembly** (`inProcess` plugins do not need it — F5 already attached the debugger) |
| `--data-root <dir>` | The debug instance uses its own data root, so your everyday VelaShell can stay open — sharing one triggers the single-instance guard and the second instance exits |

Options:

| Option | Default | Description |
| --- | --- | --- |
| `--host <version or path>` | most recently started | Pick one of several registered installations (release + preview) |
| `--exe <path>` | — | Use this executable directly, skipping the registry (portable builds, CI, never-started builds) |
| `-o, --output <dir>` | newest directory under `bin/` containing `plugin.json` | Plugin build output directory |
| `--data-root <dir>` | `~/.velashell-dev` | Data root for the debug instance |
| `--shared-data` | off | Use the everyday data root instead (**quit the running VelaShell first**) |
| `--no-wait-debugger` | off | Do not pass `--wait-debugger` |
| `--watch` | off | Also pass `--dev-watch` (auto-reload after a rebuild) |
| `--profile <name>` | `VelaShell` | Launch profile name |
| `--link` | off | Also register the development root in `plugins.dev.txt` |

### `dev run`

```bash
vela-plugin dev run [projectDir] [--wait] [--wait-debugger] [--watch]
                    [--data-root <dir>] [--shared-data] [--host <…>] [--exe <…>]
```

Starts the host with the same arguments and prints its pid; `--wait` waits for it to exit and
forwards the exit code (useful for CI smoke scripts). Note that there is **no debugger** on this
path — for breakpoints use `dev init` plus F5.

### `dev list` / `dev prune`

```bash
vela-plugin dev list     # list the roots in plugins.dev.txt and their state
vela-plugin dev prune    # drop the ones that no longer exist
```

### `dev link` / `dev unlink`

```bash
vela-plugin dev link   bin/Debug/net11.0     # old name dev-link, still works
vela-plugin dev unlink bin/Debug/net11.0
```

Writes a directory into `~/.velashell/plugins.dev.txt`, which applies to **every VelaShell
instance, permanently**. When given a plugin directory it moves up one level automatically
(the host scans a root's immediate sub-directories).

Which one to use:

- **`dev init` (recommended)**: mounting travels with the project; two projects or two branches
  never interfere.
- **`dev link`**: you want your everyday VelaShell to carry this plugin permanently.

---

## 4. Environment checks

### `hosts`

```bash
vela-plugin hosts [--all]
```

Lists registered installations, most recently started first (`--all` includes ones whose
executable is gone). At most 8 entries are kept; missing executables are pruned on the next
registration.

### `doctor`

```bash
vela-plugin doctor [projectDir] [--host <…>] [--exe <…>]
```

| Check | What a failure means |
| --- | --- |
| A host is registered | VelaShell has never been started, or you use a portable copy → pass `--exe` |
| `apiLevel` ≤ host | The plugin cannot be loaded at all |
| `minSdkVersion` ≤ host's bundled SDK | It will be marked Incompatible |
| `minHostVersion` ≤ host version | It will be marked Incompatible |
| Isolated plugin + host ships PluginHost | Isolated mode cannot run |
| `plugin.json` in the output directory | The host discovers plugins by that file; without it the plugin does not exist |
| Entry assembly present | You forgot to build |
| `.deps.json` next to the entry | `EnableDynamicLoading` is missing; none of the plugin's own NuGet dependencies resolve at runtime |
| No `VelaShell.PluginSdk.dll` / `Avalonia*.dll` in the output | Probably bypassed `VelaShell.PluginSdk.Build`; the loader always shares the host's copy, so these only bloat the package |
| Launch profile configured | Still holds the `%VELASHELL_EXE%` placeholder → run `dev init` |

Exits with `1` when a blocking problem is found (fits in CI).

---

## 5. Manifest and packaging

### `validate`

```bash
vela-plugin validate [dir|plugin.json]
```

Validates the manifest with **the same rules the host applies at load time** and confirms the
entry assembly exists. `VelaShell.PluginSdk.Build` already runs it after each build
(incrementally), so you rarely need to call it by hand.

### `pack`

```bash
vela-plugin pack <outputDir> [-o <output>] [-k <key.pem>] [--no-mask]
```

The one-step equivalent is `dotnet build -c Release -t:PackVpx` (result:
`bin/vpx/<id>-<version>.vpx`). `-o` accepts a directory (conventional file name) or a full path.
`--no-mask` disables the payload mask — **diagnostics only**; the payload then is a plain zip.

### `sign`

```bash
vela-plugin sign <pkg.vpx> -k <key.pem> [-o <output>]
```

Adds or replaces the signature (in place by default). The signature covers the 64-byte header,
which contains the payload length and digest — equivalent to signing the whole package.

### `verify`

```bash
vela-plugin verify <pkg.vpx> [-k <base64 public key>]
```

Without `-k` this only proves the signature is self-consistent (**not that the publisher is
trusted**); with `-k` the signature must come from that key. Exit code `1` when invalid or
mismatched.

### `keygen`

```bash
vela-plugin keygen [-o <key.pem>] [--force]
```

Creates an ECDSA P-256 key pair. The private key is written as PKCS#8 PEM (mode `0600` off
Windows); the public key and fingerprint are printed.

> **Losing the private key means changing identity.** Users trust a fingerprint; a new key makes
> every existing user re-confirm trust on the next upgrade. Back it up offline, never commit it,
> and keep it in encrypted CI secrets.

### `info` / `unpack`

```bash
vela-plugin info   <pkg.vpx>          # header, signature state, manifest summary
vela-plugin info   <id>               # the marketplace listing: author, license, every version
vela-plugin unpack <pkg.vpx> [dir]    # extract (with zip-slip and zip-bomb guards)
```

`info` picks between the two by how the argument looks, exactly as [`install`](#install) does.

---

## 6. Host-side launch arguments

The arguments `dev init` writes can also be used by hand. Each has an environment-variable
equivalent, and **arguments win** (arguments travel with the project; environment variables are
machine-wide state that two projects inevitably contaminate):

| Argument | Environment variable | Description |
| --- | --- | --- |
| `--dev-root <dir>` | `VELA_PLUGIN_DEV_ROOT` (path-separator list) | Development plugin root, repeatable |
| `--wait-debugger[=<ids>]` | `VELA_PLUGIN_WAIT_DEBUGGER` (comma/semicolon list) | Isolated plugins wait for a debugger; no value means `*` (all) |
| `--data-root <dir>` | — | Data root; also switches the single-instance key and database location |
| `--dev-watch` | — | Watch development roots and reload after a rebuild |

The third source is `~/.velashell/plugins.dev.txt` (one directory per line, `#` starts a
comment). All three merge in the order: arguments → environment → list file. Development roots
are scanned **after** the regular plugin roots and first id wins.

---

## 7. Troubleshooting

**`No VelaShell installation is registered`** — VelaShell has never been started here. Start it
once, or use `dev init --exe <path>`.

**F5 shows "VelaShell is already running" and exits** — your profile uses the shared data root
(`--shared-data`). Switch back to a separate data root, or quit the everyday instance.

**Code changed but the behaviour did not** — confirm the build actually succeeded
(`vela-plugin doctor` reports the entry assembly), and confirm `--dev-root` points at the
`bin/Debug` level, not at `net11.0`.

**"DLL in use" when rebuilding on Windows** — should no longer happen: development plugins load
from a shadow copy (`~/.velashell/dev-shadow/<id>/gen-N`). If it still does, another process
(a host that did not exit, an antivirus scan) is holding the file.

**An isolated plugin vanishes when I hit a breakpoint** — you did not pass `--wait-debugger`.
For matched plugins the host relaxes the activation timeout and stops the heartbeat; otherwise a
breakpoint freezes the plugin process and two missed pings kill it.

**Which process do I attach to?** — The pid is logged, shown on the plugin manager page, and
written to `~/.velashell/logs/plugin-host-<id>.pid`.
