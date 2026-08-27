using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using VelaShell.Plugin.Cli;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Plugin.Cli.Tests;

/// <summary>
/// <c>vela-plugin install</c> 的落盘链路。
/// <para>
/// 这些用例守的是同一件事:**什么东西不该进用户的插件目录**。签名坏了的、未签名又没人点头的、
/// 声称自己是别的插件的、路径想逃出目标目录的 —— 每一条都对应一种"装上去就晚了"的后果,
/// 所以它们全部在写盘之前拦截,而不是靠事后发现。
/// </para>
/// </summary>
[TestClass]
[TestCategory("Plugins")]
public class PluginInstallerTests
{
    private string _work = null!;
    private string _pluginsRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _work = Path.Combine(Path.GetTempPath(), "velashell-cli-tests", Guid.NewGuid().ToString("N"));
        _pluginsRoot = Path.Combine(_work, "plugins");
        Directory.CreateDirectory(_pluginsRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响结论。
        }
    }

    // ---- 夹具 -------------------------------------------------------------

    /// <summary>造一个最小插件目录(清单 + 假入口 + 一个可辨认的附带文件)。</summary>
    private string CreatePluginDirectory(string id = "acme.tool", string version = "1.0.0", string? extraFile = null)
    {
        string directory = Path.Combine(_work, "src-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.json"), $$"""
            { "id": "{{id}}", "version": "{{version}}", "displayName": "Tool", "entry": "Fake.dll" }
            """);
        File.WriteAllBytes(Path.Combine(directory, "Fake.dll"), [.. Enumerable.Range(0, 2048).Select(i => (byte)i)]);
        if (extraFile is not null)
        {
            File.WriteAllText(Path.Combine(directory, extraFile), "marker");
        }
        return directory;
    }

    private string Pack(string id = "acme.tool", string version = "1.0.0", ECDsa? key = null, string? extraFile = null)
    {
        string source = CreatePluginDirectory(id, version, extraFile);
        string package = Path.Combine(_work, $"{id}-{version}-{Guid.NewGuid():N}.vpx");
        VpxContainer.Pack(source, package, new VpxPackOptions { SigningKey = key });
        return package;
    }

    private InstallOptions Options(bool allowUnsigned = true, string? trust = null, bool force = false) => new()
    {
        PluginsRoot = _pluginsRoot,
        AllowUnsigned = allowUnsigned,
        RequiredFingerprint = trust,
        Force = force,
        Source = "test"
    };

    // ---- 签名策略 ---------------------------------------------------------

    [TestMethod]
    public void Install_Unsigned_IsRefusedWithoutAnExplicitOptIn()
    {
        string package = Pack();

        CliException error = Assert.ThrowsExactly<CliException>(
            () => PluginInstaller.Install(package, Options(allowUnsigned: false)));

        StringAssert.Contains(error.Message, "--allow-unsigned", StringComparison.Ordinal);
        Assert.IsFalse(Directory.Exists(Path.Combine(_pluginsRoot, "acme.tool")),
            "被拒的包不该留下任何目录");
    }

    [TestMethod]
    public void Install_Unsigned_IsAllowedWithTheOptIn_AndTheRecordSaysSo()
    {
        InstallResult result = PluginInstaller.Install(Pack(), Options());

        Assert.AreEqual(VpxSignatureState.Unsigned, result.Signature);
        InstallRecord? record = PluginInstaller.ReadRecord(result.Directory);
        Assert.IsNotNull(record);
        Assert.AreEqual("Unsigned", record.Signature);
        Assert.IsNull(record.PublisherFingerprint, "未签名包没有发布者指纹可记");
    }

    [TestMethod]
    public void Install_Signed_RecordsThePublisherFingerprint()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string expected = VpxContainer.PublicKeyFingerprint(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

        InstallResult result = PluginInstaller.Install(Pack(key: key), Options(allowUnsigned: false));

        Assert.AreEqual(VpxSignatureState.Trusted, result.Signature);
        Assert.AreEqual(expected, PluginInstaller.ReadRecord(result.Directory)?.PublisherFingerprint);
    }

    [TestMethod]
    public void Install_Trust_AcceptsTheMatchingPublisherAndRefusesAnyOther()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string fingerprint = VpxContainer.PublicKeyFingerprint(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        string package = Pack(key: key);

        // 指纹大小写不该影响结论:用户是从 README 上抄过来的。
        PluginInstaller.Install(package, Options(trust: fingerprint.ToUpperInvariant()));

        CliException error = Assert.ThrowsExactly<CliException>(() => PluginInstaller.Install(
            package, Options(trust: "SHA256:0000", force: true)));
        StringAssert.Contains(error.Message, "Publisher mismatch", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Install_Trust_OnAnUnsignedPackage_IsRefusedEvenWithAllowUnsigned()
    {
        // --allow-unsigned 表达的是"我不在乎有没有签名",--trust 表达的是"必须是这个人签的"。
        // 两个一起给时后者必须赢,否则 --trust 在 CI 里就是一句空话。
        CliException error = Assert.ThrowsExactly<CliException>(
            () => PluginInstaller.Install(Pack(), Options(trust: "SHA256:0000")));

        StringAssert.Contains(error.Message, "not signed", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Install_TamperedContainer_IsRefused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string package = Pack(key: key);
        byte[] bytes = File.ReadAllBytes(package);
        // 动载荷中段的一个字节:头部里的摘要因此对不上,容器层就该拦下。
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(package, bytes);

        Assert.ThrowsExactly<VpxFormatException>(() => PluginInstaller.Install(package, Options()));
        Assert.IsFalse(Directory.Exists(Path.Combine(_pluginsRoot, "acme.tool")));
    }

    // ---- 身份一致性 -------------------------------------------------------

    [TestMethod]
    public void Install_PackageClaimingAnotherId_IsRefused()
    {
        // 从商店按 id 装时,包内清单必须就是那个 id —— 否则商店上架一个 id、
        // 包里写另一个 id,就能顶替掉用户已装的插件。
        CliException error = Assert.ThrowsExactly<CliException>(
            () => PluginInstaller.Install(Pack(id: "acme.tool"), Options(), expectedId: "other.plugin"));

        StringAssert.Contains(error.Message, "another id", StringComparison.Ordinal);
        Assert.IsFalse(Directory.Exists(Path.Combine(_pluginsRoot, "acme.tool")));
    }

    [TestMethod]
    public void Install_PackageClaimingAnotherVersion_IsRefused()
    {
        Assert.ThrowsExactly<CliException>(() => PluginInstaller.Install(
            Pack(version: "1.0.0"), Options(), expectedId: "acme.tool", expectedVersion: "2.0.0"));
    }

    // ---- 覆盖与回滚 -------------------------------------------------------

    [TestMethod]
    public void Install_SameVersionTwice_IsRefusedUnlessForced()
    {
        string package = Pack();
        PluginInstaller.Install(package, Options());

        CliException error = Assert.ThrowsExactly<CliException>(() => PluginInstaller.Install(package, Options()));
        StringAssert.Contains(error.Message, "--force", StringComparison.Ordinal);

        Assert.AreEqual("1.0.0", PluginInstaller.Install(package, Options(force: true)).Version);
    }

    [TestMethod]
    public void Install_NewerVersion_ReplacesTheDirectoryWholesale()
    {
        PluginInstaller.Install(Pack(version: "1.0.0", extraFile: "only-in-1.0.txt"), Options());

        InstallResult result = PluginInstaller.Install(Pack(version: "1.1.0", extraFile: "only-in-1.1.txt"), Options());

        Assert.AreEqual("1.0.0", result.PreviousVersion, "应报出被覆盖掉的版本");
        Assert.AreEqual("1.1.0", result.Version);
        Assert.IsTrue(File.Exists(Path.Combine(result.Directory, "only-in-1.1.txt")));
        // 换名而不是"解压覆盖":旧版本删掉的文件必须真的消失,否则插件会读到上一版的残留。
        Assert.IsFalse(File.Exists(Path.Combine(result.Directory, "only-in-1.0.txt")),
            "旧版本独有的文件应随目录整体替换而消失");
    }

    [TestMethod]
    public void Install_LeavesNoStagingDirectoriesBehind()
    {
        PluginInstaller.Install(Pack(version: "1.0.0"), Options());
        PluginInstaller.Install(Pack(version: "1.1.0"), Options());

        string staging = Path.Combine(_pluginsRoot, ".staging");
        Assert.IsTrue(!Directory.Exists(staging) || Directory.GetDirectories(staging).Length == 0,
            "中转区在成功安装之后应当是空的");
    }

    [TestMethod]
    public void List_SeesInstalledPlugins_AndIgnoresTheStagingDirectory()
    {
        PluginInstaller.Install(Pack(id: "acme.tool"), Options());
        // 没有 plugin.json 的子目录不是插件:中转区、别人手滑建的目录都属此列。
        Directory.CreateDirectory(Path.Combine(_pluginsRoot, ".staging", "leftover"));
        Directory.CreateDirectory(Path.Combine(_pluginsRoot, "not-a-plugin"));

        IReadOnlyList<InstalledPlugin> installed = PluginInstaller.List(_pluginsRoot);

        Assert.AreEqual(1, installed.Count);
        Assert.AreEqual("acme.tool", installed[0].Manifest.Id);
        Assert.AreEqual("test", installed[0].Record?.Source);
    }

    [TestMethod]
    public void Uninstall_RemovesTheDirectory_AndIsQuietWhenNothingIsInstalled()
    {
        PluginInstaller.Install(Pack(id: "acme.tool", version: "1.2.3"), Options());

        Assert.AreEqual("1.2.3", PluginInstaller.Uninstall("acme.tool", _pluginsRoot));
        Assert.IsFalse(Directory.Exists(Path.Combine(_pluginsRoot, "acme.tool")));
        Assert.IsNull(PluginInstaller.Uninstall("acme.tool", _pluginsRoot), "再卸一次应报「没装」而不是抛");
    }

    [TestMethod]
    public void ReadRecord_ToleratesACorruptRecord()
    {
        InstallResult result = PluginInstaller.Install(Pack(), Options());
        File.WriteAllText(Path.Combine(result.Directory, PluginInstaller.RecordFileName), "{ not json");

        // 记录坏了不该让 list / update 整条命令炸掉 —— 插件本身还是能跑的。
        Assert.IsNull(PluginInstaller.ReadRecord(result.Directory));
        Assert.AreEqual(1, PluginInstaller.List(_pluginsRoot).Count);
    }

    [TestMethod]
    public void InstallRecord_IsNotPickedUpAsAPluginManifest()
    {
        InstallResult result = PluginInstaller.Install(Pack(), Options());
        string record = Path.Combine(result.Directory, PluginInstaller.RecordFileName);

        Assert.IsTrue(File.Exists(record));
        Assert.AreNotEqual(PluginManifestReader.FileName, Path.GetFileName(record),
            "安装记录绝不能叫 plugin.json,否则会把真清单顶掉");
        // 也要能被自己读回来:list / update 都靠它。
        Assert.AreEqual("acme.tool", JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(record))!
            .RootElement.GetProperty("id").GetString());
    }

    // ---- id 与解压防护 ----------------------------------------------------

    [TestMethod]
    [DataRow("velashell.redis", true)]
    [DataRow("a", true)]
    [DataRow("acme.my-plugin", true)]
    [DataRow("", false)]
    [DataRow("..", false)]
    [DataRow("../etc", false)]
    [DataRow("a/b", false)]
    [DataRow("a\\b", false)]
    [DataRow("C:", false)]
    [DataRow("Acme.Tool", false)]
    [DataRow(".hidden", false)]
    [DataRow("trailing-", false)]
    public void IsValidId_AcceptsOnlyWhatIsSafeAsADirectoryNameAndUrlSegment(string id, bool expected) =>
        // id 直接变成目录名与 URL 段,所以这条规则是路径安全的一部分,不只是风格约束。
        Assert.AreEqual(expected, PluginInstaller.IsValidId(id), $"'{id}'");

    [TestMethod]
    public void IsValidId_RejectsAnythingLongerThanSixtyFourCharacters() =>
        Assert.IsFalse(PluginInstaller.IsValidId(new string('a', 65)));

    [TestMethod]
    public void ExtractZipSafely_RefusesAnEntryThatEscapesTheDestination()
    {
        string zipPath = Path.Combine(_work, "evil.zip");
        using (FileStream file = File.Create(zipPath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(archive.CreateEntry("../escaped.txt").Open());
            writer.Write("pwned");
        }
        string destination = Path.Combine(_work, "unpacked");
        Directory.CreateDirectory(destination);

        using ZipArchive evil = ZipFile.OpenRead(zipPath);
        CliException error = Assert.ThrowsExactly<CliException>(
            () => PluginInstaller.ExtractZipSafely(evil, destination));

        StringAssert.Contains(error.Message, "escapes the destination", StringComparison.Ordinal);
        Assert.IsFalse(File.Exists(Path.Combine(_work, "escaped.txt")));
    }
}
