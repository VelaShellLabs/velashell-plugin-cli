using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Plugin.Cli;

/// <summary>
/// 装包时落在插件目录里的**安装记录**。
/// <para>
/// 它**不是**宿主那份受保护安装收据 —— 那一份带完整性保护、由宿主在装包时写下,
/// 用来发现"装完之后文件被别的程序掉包"。本文件是给 <c>vela-plugin</c> 自己看的:
/// 这个目录是哪来的、装的哪版、包的摘要是什么、发布者指纹是什么。
/// 它让 <c>list</c> / <c>update</c> / 重装判定有据可依,但**任何本地进程都能改它**,
/// 所以不要把它当安全边界。
/// </para>
/// </summary>
internal sealed record InstallRecord
{
    /// <summary>插件 id。</summary>
    public string Id { get; init; } = "";

    /// <summary>已装版本。</summary>
    public string Version { get; init; } = "";

    /// <summary>来源:商店根地址,或本地包的绝对路径。</summary>
    public string? Source { get; init; }

    /// <summary>整个 <c>.vpx</c> 文件的 SHA-256。</summary>
    public string? FileSha256 { get; init; }

    /// <summary>容器内载荷的 SHA-256。</summary>
    public string? PayloadSha256 { get; init; }

    /// <summary>装包时的签名结论。</summary>
    public string? Signature { get; init; }

    /// <summary>发布者公钥指纹(<c>SHA256:…</c>);未签名包为空。</summary>
    public string? PublisherFingerprint { get; init; }

    /// <summary>安装时间(UTC)。</summary>
    public DateTimeOffset InstalledAt { get; init; }

    /// <summary>写下这条记录的工具版本。</summary>
    public string? InstalledBy { get; init; }
}

/// <summary>一个已安装插件:目录 + 清单 + 安装记录(记录可能没有,手工放目录的就没有)。</summary>
/// <param name="Directory">插件目录。</param>
/// <param name="Manifest">目录里的 <c>plugin.json</c>。</param>
/// <param name="Record">安装记录,手工放进去的目录为 <see langword="null" />。</param>
internal sealed record InstalledPlugin(string Directory, PluginManifest Manifest, InstallRecord? Record);

/// <summary>装包时的策略开关。</summary>
internal sealed record InstallOptions
{
    /// <summary>安装根目录,默认 <c>~/.velashell/plugins</c>。</summary>
    public required string PluginsRoot { get; init; }

    /// <summary>允许装未签名的包(非交互场景必须显式给,交互时可以现场确认)。</summary>
    public bool AllowUnsigned { get; init; }

    /// <summary>要求签名者指纹必须等于此值(<c>SHA256:…</c>,大小写不敏感)。</summary>
    public string? RequiredFingerprint { get; init; }

    /// <summary>同版本也重装。</summary>
    public bool Force { get; init; }

    // 这里刻意**没有**一个笼统的 --yes:唯一会问人的地方是"这个包没签名",
    // 而那一问必须由 --allow-unsigned 单独回答。一个能把它一并答掉的通用开关,
    // 迟早会被人写进脚本里,于是未签名包就再也拦不住了。

    /// <summary>写进安装记录的来源标识。</summary>
    public string? Source { get; init; }
}

/// <summary>一次安装的结果。</summary>
/// <param name="Id">插件 id。</param>
/// <param name="Version">装上去的版本。</param>
/// <param name="PreviousVersion">被覆盖掉的版本;全新安装为 <see langword="null" />。</param>
/// <param name="Directory">插件目录。</param>
/// <param name="Info">包头信息。</param>
/// <param name="Signature">签名结论。</param>
internal sealed record InstallResult(
    string Id,
    string Version,
    string? PreviousVersion,
    string Directory,
    VpxPackageInfo Info,
    VpxSignatureState Signature);

/// <summary>
/// 把 <c>.vpx</c> 装进用户插件目录(<c>~/.velashell/plugins/&lt;id&gt;/</c>),以及与之对称的
/// 卸载与列举。
/// <para>
/// 这条路径与宿主"插件管理页 → 安装 .vpx…"落到的是**同一个目录**,宿主启动时按目录扫描
/// (见 dev-guide 的"方式二:直接放目录"),因此装完重启即生效。两者的差别只有一处:
/// 宿主那条路径还会写一份**受保护的安装收据**用于事后防篡改,而收据的密钥与格式都在宿主
/// 进程里,CLI 造不出来 —— 所以经 CLI 装的插件没有那层事后保护。作为交换,这里把能在
/// 装之前做完的检查都做足:文件摘要、容器摘要、签名、清单与 id 一致性、宿主兼容性。
/// </para>
/// </summary>
internal static partial class PluginInstaller
{
    /// <summary>安装记录的文件名。点开头,不会被宿主的插件目录扫描当成子插件。</summary>
    public const string RecordFileName = ".vela-install.json";

    /// <summary>默认安装根:与宿主的"用户安装插件"目录同一个。</summary>
    public static string DefaultPluginsRoot => Path.Combine(Program.DataRoot, "plugins");

    /// <summary>下载缓存目录。同一个包重装/降级不必再下一遍。</summary>
    public static string CacheRoot => Path.Combine(Program.DataRoot, "cache", "vpx");

    /// <summary>
    /// 换目录用的中转区,放在插件根**里面**。两个原因:
    /// 一是与目标目录必然同卷,<c>Directory.Move</c> 因此是原子换名而不是逐文件拷贝;
    /// 二是它跟着 <c>--prefix</c> 走,不会在用户的真实数据根里留下痕迹。
    /// 宿主扫插件时看的是"一级子目录里有没有 plugin.json",而清单在这里是二级,
    /// 所以中转中的插件不会被半途装载。
    /// </summary>
    /// <param name="pluginsRoot">安装根目录。</param>
    private static string StagingRootFor(string pluginsRoot) => Path.Combine(pluginsRoot, ".staging");

    private static readonly JsonSerializerOptions RecordJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>与清单里那条同一口径的插件 id 规则。命令行传进来的 id 会进路径与 URL,先卡住。</summary>
    [GeneratedRegex("^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$")]
    private static partial Regex IdPattern();

    /// <summary>id 是否合法(小写 <c>[a-z0-9.-]</c>、首尾字母数字、不超过 64 字符)。</summary>
    /// <param name="id">待校验的 id。</param>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && IdPattern().IsMatch(id);

    /// <summary>校验 id 并在不合法时抛出可读错误。</summary>
    /// <param name="id">待校验的 id。</param>
    public static string RequireValidId(string? id) =>
        IsValidId(id)
            ? id!
            : throw new CliException(
                $"'{id}' is not a valid plugin id: lowercase [a-z0-9.-], starting and ending with a letter or digit, "
                + "at most 64 characters (e.g. \"velashell.redis\").");

    /// <summary>
    /// 把一个 <c>.vpx</c> 装进 <see cref="InstallOptions.PluginsRoot" />。
    /// 顺序刻意是"全部检查 → 解到中转区 → 换名":任何一步失败都不会留下半个插件目录。
    /// </summary>
    /// <param name="packagePath"><c>.vpx</c> 路径。</param>
    /// <param name="options">策略开关。</param>
    /// <param name="expectedId">期望的插件 id(从商店装时给);包内清单与它不符即拒装。</param>
    /// <param name="expectedVersion">期望的版本号(从商店装时给)。</param>
    public static InstallResult Install(string packagePath, InstallOptions options,
        string? expectedId = null, string? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 1) 容器完整性。OpenPayload 自己会核对头部 CRC、长度与载荷摘要,打不开就是坏包。
        using Stream payload = VpxContainer.OpenPayload(packagePath, out VpxPackageInfo info);

        // 2) 签名。策略与宿主一致:坏签名一律拒;未签名要人明确点头;签名有效则报出指纹。
        VpxSignatureState signature = VpxContainer.VerifySignature(info);
        string? fingerprint = info.Signature is { } block ? VpxContainer.PublicKeyFingerprint(block.PublicKey) : null;
        CheckSignature(signature, fingerprint, options);

        // 3) 清单。先读出来才知道要装到哪个目录、以及它是不是我们要的那个插件。
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        PluginManifest manifest = ReadManifest(archive, packagePath);
        if (expectedId is not null && !manifest.Id.Equals(expectedId, StringComparison.Ordinal))
        {
            throw new CliException($"The package declares plugin id '{manifest.Id}' but '{expectedId}' was requested. "
                                   + "Refusing to install: a package must not install itself under another id.");
        }
        if (expectedVersion is not null && !manifest.Version.Equals(expectedVersion, StringComparison.Ordinal))
        {
            throw new CliException($"The package declares version '{manifest.Version}' but the marketplace listed "
                                   + $"'{expectedVersion}'.");
        }

        // 4) 宿主兼容性。装不上去的包早点说,别等到宿主启动后在管理页里显示 Incompatible。
        string directory = Path.Combine(options.PluginsRoot, manifest.Id);
        InstallRecord? previous = ReadRecord(directory);
        string? previousVersion = previous?.Version
                                  ?? (File.Exists(Path.Combine(directory, PluginManifestReader.FileName))
                                      ? TryReadManifest(directory)?.Version
                                      : null);
        if (!options.Force && previousVersion is not null
            && previousVersion.Equals(manifest.Version, StringComparison.Ordinal))
        {
            throw new CliException($"{manifest.Id} v{manifest.Version} is already installed in '{directory}'. "
                                   + "Pass --force to reinstall.");
        }
        RequireHostIsNotHoldingTheDirectory(directory);

        // 5) 解包到中转区,确认入口程序集真的在里面,再换名到位。
        string stagingRoot = StagingRootFor(options.PluginsRoot);
        Directory.CreateDirectory(stagingRoot);
        string staging = Path.Combine(stagingRoot, $"{manifest.Id}-{Guid.CreateVersion7():n}");
        try
        {
            Directory.CreateDirectory(staging);
            ExtractZipSafely(archive, staging);
            if (!File.Exists(Path.Combine(staging, manifest.Entry)))
            {
                throw new CliException($"The package's entry assembly '{manifest.Entry}' is not inside the package.");
            }
            WriteRecord(staging, new InstallRecord
            {
                Id = manifest.Id,
                Version = manifest.Version,
                Source = options.Source,
                FileSha256 = FileDigest(packagePath),
                PayloadSha256 = info.PayloadSha256,
                Signature = signature.ToString(),
                PublisherFingerprint = fingerprint,
                InstalledAt = DateTimeOffset.UtcNow,
                InstalledBy = $"vela-plugin {Program.ToolVersion}"
            });
            Swap(staging, directory, stagingRoot);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
        return new InstallResult(manifest.Id, manifest.Version, previousVersion, directory, info, signature);
    }

    /// <summary>删掉一个已安装插件的目录。返回它原来的版本,没装过则返回 <see langword="null" />。</summary>
    /// <param name="id">插件 id。</param>
    /// <param name="pluginsRoot">安装根目录。</param>
    public static string? Uninstall(string id, string pluginsRoot)
    {
        string directory = Path.Combine(pluginsRoot, RequireValidId(id));
        if (!Directory.Exists(directory))
        {
            return null;
        }
        string? version = ReadRecord(directory)?.Version ?? TryReadManifest(directory)?.Version ?? "(unknown)";
        RequireHostIsNotHoldingTheDirectory(directory);
        // 先换名再删:换名失败(文件被占用)时目录还完整,不会留下删了一半的插件。
        string stagingRoot = StagingRootFor(pluginsRoot);
        Directory.CreateDirectory(stagingRoot);
        string condemned = Path.Combine(stagingRoot, $"removed-{id}-{Guid.CreateVersion7():n}");
        Directory.Move(directory, condemned);
        TryDeleteDirectory(condemned);
        return version;
    }

    /// <summary>列出安装根下的插件(按 id 排序)。读不出清单的子目录跳过。</summary>
    /// <param name="pluginsRoot">安装根目录。</param>
    public static IReadOnlyList<InstalledPlugin> List(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            return [];
        }
        var result = new List<InstalledPlugin>();
        foreach (string directory in Directory.EnumerateDirectories(pluginsRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (TryReadManifest(directory) is { } manifest)
            {
                result.Add(new InstalledPlugin(directory, manifest, ReadRecord(directory)));
            }
        }
        return result;
    }

    /// <summary>读插件目录里的安装记录;没有或读不动都返回 <see langword="null" />。</summary>
    /// <param name="directory">插件目录。</param>
    public static InstallRecord? ReadRecord(string directory)
    {
        string path = Path.Combine(directory, RecordFileName);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path), RecordJson);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 记录坏了不影响插件本身能不能跑,当作"没有记录"即可。
            return null;
        }
    }

    /// <summary>缓存里这个版本的包路径(不保证存在)。</summary>
    /// <param name="id">插件 id。</param>
    /// <param name="version">版本号。</param>
    public static string CachePath(string id, string version) =>
        Path.Combine(CacheRoot, $"{id}-{version}{VpxContainer.FileExtension}");

    /// <summary>整文件 SHA-256(小写十六进制)。</summary>
    /// <param name="path">文件路径。</param>
    public static string FileDigest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>签名策略。与宿主同一张表:坏的一律拒、未签名要点头、有效的报指纹。</summary>
    private static void CheckSignature(VpxSignatureState state, string? fingerprint, InstallOptions options)
    {
        if (state == VpxSignatureState.Invalid)
        {
            throw new CliException("The package signature does not verify - the container has been modified since it "
                                   + "was signed. There is no override for this.");
        }
        if (options.RequiredFingerprint is { } required)
        {
            if (fingerprint is null)
            {
                throw new CliException($"--trust {required} was given but the package is not signed at all.");
            }
            if (!fingerprint.Equals(required, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException($"Publisher mismatch: the package is signed by {fingerprint}, not {required}.");
            }
            return;
        }
        if (state != VpxSignatureState.Unsigned)
        {
            return;
        }
        if (options.AllowUnsigned)
        {
            Program.Warn("this package is not signed; nothing ties it to a publisher. Installing anyway (--allow-unsigned).");
            return;
        }
        // 交互时现场问一句,非交互时要求显式开关 —— 脚本不该因为"没人回答"而默默装了未签名包。
        if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            Console.WriteLine("This package is NOT signed. A plugin runs with your account's privileges, and nothing");
            Console.WriteLine("here proves who built it. Install it only if you trust where it came from.");
            Console.Write("Install anyway? [y/N] ");
            string? answer = Console.ReadLine();
            if (answer?.Trim() is "y" or "Y" or "yes" or "YES" or "Yes")
            {
                return;
            }
            throw new CliException("Aborted.");
        }
        throw new CliException("The package is not signed. Pass --allow-unsigned to install it anyway "
                               + "(a plugin runs with your account's privileges).");
    }

    /// <summary>
    /// 宿主活着的时候不要动它已经装载的插件目录:Windows 上入口 dll 被 ALC 锁着,
    /// 换名会以一条与本命令无关的 <c>IOException</c> 收场。生产路径不走影子拷贝(那是开发期专有),
    /// 所以这里没有取巧余地 —— 先说清楚,比让人去猜"文件正被另一进程使用"强。
    /// </summary>
    private static void RequireHostIsNotHoldingTheDirectory(string directory)
    {
        if (!Directory.Exists(directory) || Process.GetProcessesByName("VelaShell").Length == 0)
        {
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            throw new CliException($"VelaShell is running and '{Path.GetFileName(directory)}' is already installed; "
                                   + "Windows keeps the loaded assemblies locked. Quit VelaShell and run this again.");
        }
        Program.Warn("VelaShell is running. The files are replaced underneath it; restart the host to pick up the change.");
    }

    /// <summary>把中转目录换到位。已有目录先移走做备份,换名失败就原样搬回来。</summary>
    private static void Swap(string staging, string directory, string stagingRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        string? backup = null;
        if (Directory.Exists(directory))
        {
            backup = Path.Combine(stagingRoot, $"backup-{Path.GetFileName(directory)}-{Guid.CreateVersion7():n}");
            Directory.Move(directory, backup);
        }
        try
        {
            Directory.Move(staging, directory);
        }
        catch
        {
            if (backup is not null)
            {
                // 换名失败时旧版本还在备份里。搬不回去才是真的丢东西,所以这里不吞异常地尽力恢复。
                Directory.Move(backup, directory);
            }
            throw;
        }
        if (backup is not null)
        {
            TryDeleteDirectory(backup);
        }
    }

    private static void WriteRecord(string directory, InstallRecord record) =>
        File.WriteAllText(Path.Combine(directory, RecordFileName), JsonSerializer.Serialize(record, RecordJson));

    private static PluginManifest ReadManifest(ZipArchive archive, string packagePath)
    {
        ZipArchiveEntry entry = archive.GetEntry(PluginManifestReader.FileName)
                                ?? throw new CliException(
                                    $"'{packagePath}' has no {PluginManifestReader.FileName} - it is not a plugin package.");
        using StreamReader reader = new(entry.Open());
        return PluginManifestReader.Parse(reader.ReadToEnd());
    }

    private static PluginManifest? TryReadManifest(string directory)
    {
        string path = Path.Combine(directory, PluginManifestReader.FileName);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return PluginManifestReader.Load(path);
        }
        catch (Exception ex) when (ex is PluginManifestException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 中转/备份目录留在 cache 下不影响任何事,下次装同一个插件会用新的名字。
        }
    }

    private const int MaxUnpackEntries = 10_000;
    private const long MaxUnpackedBytes = 512L * 1024 * 1024;

    /// <summary>
    /// 解 zip,同时挡住 zip-slip(条目路径逃出目标目录)、符号链接与解压炸弹。
    /// <c>unpack</c> 与 <c>install</c> 共用这一份 —— 排障命令与安装路径的防护不该有强弱之分。
    /// </summary>
    /// <param name="archive">已打开的 zip。</param>
    /// <param name="destination">目标目录。</param>
    public static void ExtractZipSafely(ZipArchive archive, string destination)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Entries.Count > MaxUnpackEntries)
        {
            throw new CliException($"Package contains too many entries ({archive.Entries.Count}; limit {MaxUnpackEntries}).");
        }
        string root = Path.GetFullPath(destination + Path.DirectorySeparatorChar);
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        long remaining = MaxUnpackedBytes;
        byte[] buffer = new byte[64 * 1024];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new CliException($"Package contains a symbolic link: {entry.FullName}");
            }
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, pathComparison))
            {
                throw new CliException($"Package entry escapes the destination: {entry.FullName}");
            }
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream source = entry.Open();
            using FileStream output = File.Create(target);
            int read;
            while ((read = source.Read(buffer)) > 0)
            {
                remaining -= read;
                if (remaining < 0)
                {
                    throw new CliException($"Package expands beyond the {MaxUnpackedBytes / (1024 * 1024)} MB limit.");
                }
                output.Write(buffer, 0, read);
            }
        }
    }
}
