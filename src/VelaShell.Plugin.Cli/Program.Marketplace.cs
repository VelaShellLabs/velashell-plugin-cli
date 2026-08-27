using System.Diagnostics;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Hosting;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Plugin.Cli;

/// <summary>
/// 从插件商店装包的那一族命令:<c>install</c> / <c>uninstall</c> / <c>list</c> /
/// <c>search</c> / <c>update</c>,外加 <c>info &lt;id&gt;</c> 的商店查询分支。
/// <para>
/// 与宿主"插件管理页 → 安装 .vpx…"落到同一个目录(<c>~/.velashell/plugins/&lt;id&gt;/</c>),
/// 差别与取舍写在 <see cref="PluginInstaller" /> 的类型注释里。
/// </para>
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// <c>install &lt;id&gt;[@&lt;版本&gt;]</c> 从商店装,<c>install &lt;包.vpx&gt;</c> 从本地文件装。
    /// 判定方式与 npm 同理:能当文件找到、或写着 <c>.vpx</c> 后缀、或看着就是条路径,一律当本地包。
    /// </summary>
    private static int Install(string[] args)
    {
        var options = CliOptions.Parse(args);
        string target = options.Positional.FirstOrDefault()
                        ?? throw new CliException(
                            "Missing plugin. Usage: vela-plugin install <id>[@<version>] | <package.vpx>");

        var installOptions = new InstallOptions
        {
            PluginsRoot = Path.GetFullPath(options.Get("--prefix") ?? PluginInstaller.DefaultPluginsRoot),
            AllowUnsigned = options.Has("--allow-unsigned"),
            RequiredFingerprint = options.Get("--trust"),
            Force = options.Has("--force")
        };

        if (LooksLikeAPath(target))
        {
            string package = Path.GetFullPath(target);
            if (!File.Exists(package))
            {
                throw new CliException($"Package not found: {package}");
            }
            PrintInstallResult(PluginInstaller.Install(package, installOptions with { Source = package }));
            return 0;
        }
        return InstallFromMarketplace(target, options, installOptions);
    }

    /// <summary>解析 id@版本 → 查详情 → 选版本 → 下载(带缓存)→ 校验 → 装。</summary>
    private static int InstallFromMarketplace(string target, CliOptions options, InstallOptions installOptions)
    {
        (string id, string? pinned) = SplitIdAndVersion(target);
        PluginInstaller.RequireValidId(id);
        pinned ??= options.Get("--version");

        using var market = new Marketplace(options.Get("--source"));
        MarketPlugin plugin = market.Get(id);
        MarketVersion version = SelectVersion(plugin, pinned, options.Has("--pre"));

        Console.WriteLine($"{plugin.Id}  {version.Version}"
                          + (plugin.DisplayName is { Length: > 0 } name ? $"  {name}" : ""));
        Console.WriteLine($"  from       {market.BaseUrl}"
                          + ((plugin.Publisher ?? plugin.Author) is { Length: > 0 } author
                              ? $"   published by {author}"
                              : ""));

        string package = FetchPackage(market, plugin.Id, version, options);
        if (options.Has("--download-only"))
        {
            string directory = Path.GetFullPath(options.Get("--download-only") ?? ".");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, Path.GetFileName(package));
            // 缓存里那一份要留着(下次装同版本免得再下),所以是拷贝而不是搬。
            if (!string.Equals(package, destination, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(package, destination, overwrite: true);
            }
            Console.WriteLine($"  saved      {destination}");
            Console.WriteLine("Not installed (--download-only).");
            return 0;
        }

        InstallResult result = PluginInstaller.Install(package, installOptions with { Source = market.BaseUrl },
            plugin.Id, version.Version);
        PrintInstallResult(result);
        return 0;
    }

    /// <summary>
    /// 拿到本地的一份 <c>.vpx</c>:缓存里摘要对得上就直接用,否则下载并核对商店声明的整包摘要。
    /// </summary>
    private static string FetchPackage(Marketplace market, string id, MarketVersion version, CliOptions options)
    {
        string cached = PluginInstaller.CachePath(id, version.Version);
        if (!options.Has("--no-cache") && File.Exists(cached) && version.FileSha256 is { Length: > 0 } expected
            && PluginInstaller.FileDigest(cached).Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  package    {Format.Bytes(new FileInfo(cached).Length)}  (from cache)");
            return cached;
        }

        MarketDownload ticket = market.GetDownload(id, version.Version);
        Directory.CreateDirectory(PluginInstaller.CacheRoot);
        // 先下到临时名再改名:中断/失败的下载不会以"缓存里已有这个版本"的身份留下来。
        string temporary = cached + ".partial";
        string digest = market.Download(ticket, temporary, showProgress: !Console.IsErrorRedirected);

        // 商店声明的整包摘要是这条链路上唯一能挡住"直链被换掉"的东西:
        // 直链本身来自对象存储,签名只保证 URL 没被改,不保证桶里的对象没被换。
        string? declared = ticket.FileSha256 ?? version.FileSha256;
        if (declared is { Length: > 0 } && !digest.Equals(declared, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporary);
            throw new CliException($"The downloaded package hashes to {digest}, but the marketplace declared "
                                   + $"{declared}. Refusing to install it.");
        }
        if (declared is not { Length: > 0 })
        {
            Warn($"the marketplace declared no file digest for {id} {version.Version}; "
                 + "only the container's own integrity check applies.");
        }
        File.Move(temporary, cached, overwrite: true);
        Console.WriteLine($"  package    {Format.Bytes(new FileInfo(cached).Length)}  sha256 {digest[..16]}…");
        return cached;
    }

    private static void PrintInstallResult(InstallResult result)
    {
        Console.WriteLine($"  signature  {(result.Signature == VpxSignatureState.Trusted ? "Valid" : result.Signature.ToString())}"
                          + (result.Info.Signature is { } block
                              ? $"  {VpxContainer.PublicKeyFingerprint(block.PublicKey)}"
                              : ""));
        Console.WriteLine(result.PreviousVersion is { } previous && previous != result.Version
            ? $"  installed  {result.Directory}   (upgraded from {previous})"
            : $"  installed  {result.Directory}");

        // 装完还要重启才生效 —— 不说清楚的话,下一句话一定是"我装了啊怎么没有"。
        Console.WriteLine(Process.GetProcessesByName("VelaShell").Length > 0
            ? "VelaShell is running; restart it to load the plugin."
            : "Start VelaShell to load the plugin.");

        // 兼容性:能查到本机宿主就顺手核对一遍,装完才发现 Incompatible 太晚了。
        if (HostRegistry.Resolve(null) is { } host)
        {
            WarnOnHostMismatch(
                PluginManifestReader.Load(Path.Combine(result.Directory, PluginManifestReader.FileName)), host);
        }
    }

    /// <summary><c>uninstall &lt;id&gt;</c>:删目录。宿主 DB 里的插件数据**不动**(那把钥匙在宿主手里)。</summary>
    private static int Uninstall(string[] args)
    {
        var options = CliOptions.Parse(args);
        string id = options.Positional.FirstOrDefault()
                    ?? throw new CliException("Missing plugin id. Usage: vela-plugin uninstall <id>");
        string root = Path.GetFullPath(options.Get("--prefix") ?? PluginInstaller.DefaultPluginsRoot);
        if (PluginInstaller.Uninstall(id, root) is not { } version)
        {
            Console.WriteLine($"{id} is not installed in {root}.");
            return 0;
        }
        Console.WriteLine($"Removed {id} v{version} from {root}");
        // 管理页的卸载会连插件数据一起清,这里做不到:KV / 机密 / 时序库都在宿主的库里,
        // 加密与库锁都归宿主。说清楚,免得有人以为"卸载了就干净了"。
        Console.WriteLine("The plugin's stored data (KV, secrets, time series) is kept - only the host's plugin");
        Console.WriteLine("manager can clear it. Reinstalling the same id picks the data back up.");
        return 0;
    }

    /// <summary><c>list</c>:本机装了哪些、哪来的、签没签名。</summary>
    private static int ListInstalled(string[] args)
    {
        var options = CliOptions.Parse(args);
        string root = Path.GetFullPath(options.Get("--prefix") ?? PluginInstaller.DefaultPluginsRoot);
        IReadOnlyList<InstalledPlugin> installed = PluginInstaller.List(root);
        if (installed.Count == 0)
        {
            Console.WriteLine($"No plugins installed in {root}.");
            return 0;
        }
        Console.WriteLine(root);
        foreach (InstalledPlugin plugin in installed)
        {
            string origin = plugin.Record?.Source is { Length: > 0 } source
                ? (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile ? uri.Host : "local package")
                : "no install record";
            Console.WriteLine($"  {plugin.Manifest.Id,-32} {plugin.Manifest.Version,-14} {origin}");
            if (plugin.Record?.PublisherFingerprint is { Length: > 0 } fingerprint)
            {
                Console.WriteLine($"  {"",-32} {"",-14} signed {fingerprint}");
            }
        }
        Console.WriteLine($"{installed.Count} plugin(s).");
        return 0;
    }

    /// <summary><c>search &lt;词&gt;</c>:商店搜索。不给词就列前一页。</summary>
    private static int Search(string[] args)
    {
        var options = CliOptions.Parse(args);
        using var market = new Marketplace(options.Get("--source"));
        MarketPage page = market.Search(string.Join(' ', options.Positional),
            int.TryParse(options.Get("--page"), out int p) ? p : 1,
            int.TryParse(options.Get("--size"), out int s) ? s : 20);
        if (page.Items.Length == 0)
        {
            Console.WriteLine("No plugins matched.");
            return 0;
        }
        foreach (MarketListing item in page.Items)
        {
            Console.WriteLine($"{item.Id,-32} {item.LatestVersion,-14} {item.DisplayName}");
            if (item.Summary is { Length: > 0 } summary)
            {
                Console.WriteLine($"  {Truncate(summary, 96)}");
            }
        }
        Console.WriteLine($"{page.Items.Length} of {page.Total} result(s) - {market.BaseUrl}");
        return 0;
    }

    /// <summary><c>update [&lt;id&gt;]</c>:比对商店上的最新版并装上去;<c>--check</c> 只报告。</summary>
    private static int Update(string[] args)
    {
        var options = CliOptions.Parse(args);
        string root = Path.GetFullPath(options.Get("--prefix") ?? PluginInstaller.DefaultPluginsRoot);
        bool allowPrerelease = options.Has("--pre");
        IReadOnlyList<InstalledPlugin> installed = PluginInstaller.List(root);
        if (options.Positional.FirstOrDefault() is { } only)
        {
            installed = [.. installed.Where(i => i.Manifest.Id.Equals(only, StringComparison.Ordinal))];
            if (installed.Count == 0)
            {
                throw new CliException($"{only} is not installed in {root}.");
            }
        }
        if (installed.Count == 0)
        {
            Console.WriteLine($"No plugins installed in {root}.");
            return 0;
        }

        var installOptions = new InstallOptions
        {
            PluginsRoot = root,
            AllowUnsigned = options.Has("--allow-unsigned"),
            RequiredFingerprint = options.Get("--trust"),
            Force = false
        };
        using var market = new Marketplace(options.Get("--source"));
        int outdated = 0;
        int failed = 0;
        foreach (InstalledPlugin plugin in installed)
        {
            MarketVersion latest;
            try
            {
                latest = SelectVersion(market.Get(plugin.Manifest.Id), null, allowPrerelease);
            }
            catch (CliException ex)
            {
                // 商店上没有这个 id 是正常情况(手工放进去的、私有的),不该让整条命令失败。
                Console.WriteLine($"  {plugin.Manifest.Id,-32} {plugin.Manifest.Version,-14} skipped: {ex.Message}");
                continue;
            }
            if (!IsOlder(plugin.Manifest.Version, latest.Version))
            {
                continue;
            }
            outdated++;
            Console.WriteLine($"  {plugin.Manifest.Id,-32} {plugin.Manifest.Version} -> {latest.Version}");
            if (options.Has("--check"))
            {
                continue;
            }
            try
            {
                string package = FetchPackage(market, plugin.Manifest.Id, latest, options);
                PrintInstallResult(PluginInstaller.Install(package,
                    installOptions with { Source = market.BaseUrl }, plugin.Manifest.Id, latest.Version));
            }
            catch (Exception ex) when (ex is CliException or VpxFormatException or PluginManifestException or IOException)
            {
                // 一个插件装不上不该拦住其余的:全跑完再用退出码汇报。
                failed++;
                Error($"{plugin.Manifest.Id}: {ex.Message}");
            }
        }
        Console.WriteLine(outdated == 0
            ? "Everything is up to date."
            : options.Has("--check")
                ? $"{outdated} plugin(s) can be updated. Run `vela-plugin update` to apply."
                : $"{outdated - failed} of {outdated} update(s) applied.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>商店侧的插件详情(<c>info &lt;id&gt;</c> 在参数不是个包文件时走到这里)。</summary>
    private static int InfoFromMarketplace(string id, CliOptions options)
    {
        using var market = new Marketplace(options.Get("--source"));
        MarketPlugin plugin = market.Get(PluginInstaller.RequireValidId(id));
        Console.WriteLine($"{plugin.Id}  {plugin.DisplayName}");
        if (plugin.Summary is { Length: > 0 } summary)
        {
            Console.WriteLine($"  {summary}");
        }
        Console.WriteLine($"  author     {plugin.Author ?? plugin.Publisher ?? "(not set)"}");
        Console.WriteLine($"  license    {plugin.License ?? "(not set)"}");
        Console.WriteLine($"  homepage   {plugin.Homepage ?? "(not set)"}");
        Console.WriteLine($"  tags       {(plugin.Tags.Length == 0 ? "(none)" : string.Join(", ", plugin.Tags))}");
        Console.WriteLine($"  downloads  {plugin.Downloads}");
        Console.WriteLine("  versions");
        foreach (MarketVersion version in plugin.Versions)
        {
            Console.WriteLine($"    {version.Version,-16} api {version.ApiLevel}  {version.HostMode,-10} "
                              + $"{Format.Bytes(version.PackageSize),-10} {version.Signature}"
                              + (version.MinHostVersion is { Length: > 0 } minimum ? $"  needs host >= {minimum}" : ""));
        }
        Console.WriteLine($"  {market.BaseUrl}");
        Console.WriteLine($"Install with: vela-plugin install {plugin.Id}");
        return 0;
    }

    // ---- 版本挑选 ---------------------------------------------------------

    /// <summary>
    /// 选一个版本:钉了就必须有那一个;没钉则取**最高的正式版**,没有正式版才回落到预发布。
    /// 默认跳过预发布与 npm 的 <c>@latest</c> 同理 —— 发了个 preview 不该让所有人跟着升。
    /// </summary>
    internal static MarketVersion SelectVersion(MarketPlugin plugin, string? pinned, bool allowPrerelease)
    {
        if (plugin.Versions.Length == 0)
        {
            throw new CliException($"{plugin.Id} has no published versions on the marketplace.");
        }
        if (pinned is not null)
        {
            return plugin.Versions.FirstOrDefault(v => v.Version.Equals(pinned, StringComparison.Ordinal))
                   ?? throw new CliException($"{plugin.Id} has no version '{pinned}'. Available: "
                                             + string.Join(", ", plugin.Versions.Select(v => v.Version)));
        }
        MarketVersion[] ordered = [.. plugin.Versions.OrderByDescending(v => v.Version, VersionOrder.Instance)];
        return allowPrerelease
            ? ordered[0]
            : ordered.FirstOrDefault(v => !v.Version.Contains('-', StringComparison.Ordinal)) ?? ordered[0];
    }

    // ---- 小工具 -----------------------------------------------------------

    /// <summary><c>id@版本</c> 拆开。id 的字符集不含 <c>@</c>,所以第一个 <c>@</c> 就是分隔符。</summary>
    private static (string Id, string? Version) SplitIdAndVersion(string value)
    {
        int at = value.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? (value, null) : (value[..at], value[(at + 1)..]);
    }

    /// <summary>这个参数看着是路径还是商店 id。</summary>
    private static bool LooksLikeAPath(string value) =>
        value.EndsWith(VpxContainer.FileExtension, StringComparison.OrdinalIgnoreCase)
        || value.Contains('/', StringComparison.Ordinal)
        || value.Contains('\\', StringComparison.Ordinal)
        || File.Exists(value);

    private static string Truncate(string value, int length)
    {
        string flat = value.ReplaceLineEndings(" ").Trim();
        return flat.Length <= length ? flat : flat[..length] + "…";
    }
}
