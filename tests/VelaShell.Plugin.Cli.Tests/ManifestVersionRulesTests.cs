using VelaShell.Plugin.Cli;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Versioning;

namespace VelaShell.Plugin.Cli.Tests;

/// <summary>
/// 清单版本号的收紧校验(维护者决定:与 SDK 的 <see cref="SemanticVersion" /> 一致)。
/// <para>
/// 守两件事:一是接受与否和 <see cref="SemanticVersion.TryParse" /> 完全一致(不许在 CLI 里长出第二套规则);
/// 二是拒绝时说得出是哪一段、怎么改 —— 一句「版本不合法」只会让作者去翻规范。
/// </para>
/// </summary>
[TestClass]
[TestCategory("Plugins")]
public class ManifestVersionRulesTests
{
    private string _work = null!;

    [TestInitialize]
    public void Setup()
    {
        _work = Path.Combine(Path.GetTempPath(), "velashell-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
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
            // 清理失败不影响结论。
        }
    }

    [TestMethod]
    [DataRow("1.0.0")]
    [DataRow("1.2")]
    [DataRow("1.2.3.4")]
    [DataRow("01.2.3")]
    [DataRow("1.0.0-beta.1")]
    [DataRow("1.0.0-0")]
    [DataRow("1.0.0--a")]
    [DataRow("1.0.0-beta.0a")]
    [DataRow("2147483647.0.0")]
    public void Explain_AcceptsWhatSemanticVersionAccepts(string version)
    {
        Assert.IsTrue(SemanticVersion.TryParse(version, out _), "用例本身要是 SemanticVersion 认的写法");
        Assert.IsNull(ManifestVersionRules.Explain(version));
    }

    [TestMethod]
    [DataRow("1.0.0-beta.01", "'01'", "1.0.0-beta.1")]
    [DataRow("1.0.0-01", "'01'", "1.0.0-1")]
    [DataRow("1.0.0-a..b", "empty identifier", "1.0.0-a.b")]
    [DataRow("1.0.0-.a", "empty identifier", "1.0.0-a")]
    [DataRow("1.0.0-a.", "empty identifier", "1.0.0-a")]
    [DataRow("2147483648.0.0", "'2147483648'", null)]
    [DataRow("١.٠.٠", "ASCII", null)]
    [DataRow("1.0.0\n", "whitespace", "1.0.0")]
    public void Explain_NamesTheOffendingPartAndHowToFixIt(string version, string mentions, string? suggestion)
    {
        string? reason = ManifestVersionRules.Explain(version);

        Assert.IsNotNull(reason, $"'{version}' 必须被拒");
        Assert.Contains(mentions, reason, StringComparison.Ordinal);
        Assert.Contains(suggestion is null ? "Expected e.g." : $"Use e.g. \"{suggestion}\"", reason, StringComparison.Ordinal);
        if (suggestion is not null)
        {
            Assert.IsNull(ManifestVersionRules.Explain(suggestion), "给出的改法自己必须合法");
        }
    }

    [TestMethod]
    public void Require_ChecksAllThreeVersionFields()
    {
        PluginManifest manifest = PluginManifestReader.Parse(
            """{ "id": "acme.demo", "version": "1.0.0", "displayName": "Demo", "entry": "Demo.dll", "minSdkVersion": "2.1.0-rc.01" }""");

        PluginManifestException error = Assert.ThrowsExactly<PluginManifestException>(() => ManifestVersionRules.Require(manifest));

        Assert.StartsWith("Invalid minSdkVersion '2.1.0-rc.01':", error.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void LoadManifest_UsedByPackAndValidate_RejectsAVersionTheSdkRegexLetsThrough()
    {
        // 端到端地钉住接线:pack / validate 读清单走的就是 LoadManifest。
        File.WriteAllText(Path.Combine(_work, PluginManifestReader.FileName),
            """{ "id": "acme.demo", "version": "1.0.0-beta.01", "displayName": "Demo", "entry": "Demo.dll" }""");
        Assert.AreEqual("1.0.0-beta.01", PluginManifestReader.Load(Path.Combine(_work, PluginManifestReader.FileName)).Version,
            "前提:SDK 自己的清单校验放行这种写法(SDK 收紧之后这条用例连同 ManifestVersionRules 一起删)");

        PluginManifestException error = Assert.ThrowsExactly<PluginManifestException>(() => Program.LoadManifest(_work));

        Assert.Contains("Invalid version '1.0.0-beta.01'", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"1.0.0-beta.1\"", error.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Require_PrintsControlCharactersEscaped()
    {
        PluginManifest manifest = PluginManifestReader.Parse(
            """{ "id": "acme.demo", "version": "1.0.0\n", "displayName": "Demo", "entry": "Demo.dll" }""");

        PluginManifestException error = Assert.ThrowsExactly<PluginManifestException>(() => ManifestVersionRules.Require(manifest));

        Assert.Contains("'1.0.0\\u000a'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", error.Message, StringComparison.Ordinal);
    }
}
