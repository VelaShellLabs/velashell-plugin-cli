using VelaShell.Plugin.Cli;

namespace VelaShell.Plugin.Cli.Tests;

/// <summary>
/// "装哪一版"的决策。<c>install &lt;id&gt;</c> 不带版本号是最常走的一条路,选错版本的后果
/// (把所有人升到一个 preview 上)既安静又难回滚,所以这条规则要钉住。
/// </summary>
[TestClass]
[TestCategory("Plugins")]
public class VersionSelectionTests
{
    private static MarketPlugin PluginWith(params string[] versions) => new()
    {
        Id = "acme.tool",
        Versions = [.. versions.Select(v => new MarketVersion { Version = v })]
    };

    [TestMethod]
    public void SelectVersion_TakesTheHighestStableVersion_NotJustTheFirstOneListed()
    {
        // 商店给的顺序不该被当成"最新在前":这里故意乱序。
        MarketPlugin plugin = PluginWith("1.2.0", "1.10.0", "1.9.3");

        Assert.AreEqual("1.10.0", Program.SelectVersion(plugin, null, allowPrerelease: false).Version,
            "1.10 比 1.9 新 —— 按字符串比会得到相反的答案");
    }

    [TestMethod]
    public void SelectVersion_SkipsPreReleasesByDefault()
    {
        MarketPlugin plugin = PluginWith("1.4.0", "1.5.0-preview.1");

        Assert.AreEqual("1.4.0", Program.SelectVersion(plugin, null, allowPrerelease: false).Version);
        Assert.AreEqual("1.5.0-preview.1", Program.SelectVersion(plugin, null, allowPrerelease: true).Version);
    }

    [TestMethod]
    public void SelectVersion_FallsBackToAPreRelease_WhenNothingStableHasShipped()
    {
        // 只发过 preview 的新插件不该变成"装不了"。
        MarketPlugin plugin = PluginWith("0.1.0-alpha.2", "0.1.0-alpha.1");

        Assert.AreEqual("0.1.0-alpha.2", Program.SelectVersion(plugin, null, allowPrerelease: false).Version);
    }

    [TestMethod]
    public void SelectVersion_Pinned_MustExistExactly()
    {
        MarketPlugin plugin = PluginWith("1.4.0", "1.4.1");

        Assert.AreEqual("1.4.0", Program.SelectVersion(plugin, "1.4.0", allowPrerelease: false).Version);

        CliException error = Assert.ThrowsExactly<CliException>(
            () => Program.SelectVersion(plugin, "1.4", allowPrerelease: false));
        // 报错里要把可选版本列出来,否则用户只能去网页上翻。
        Assert.Contains("1.4.1", error.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SelectVersion_NoPublishedVersions_SaysSo()
    {
        CliException error = Assert.ThrowsExactly<CliException>(
            () => Program.SelectVersion(PluginWith(), null, allowPrerelease: false));

        Assert.Contains("no published versions", error.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SelectVersion_ComparesPreReleaseIdentifiersNumerically()
    {
        // 改用 SDK 的 SemanticVersion 之前,预发布后缀按序数比字符串,"beta.10" < "beta.2"。
        MarketPlugin plugin = PluginWith("2.0.0-beta.2", "2.0.0-beta.10", "2.0.0-beta.9");

        Assert.AreEqual("2.0.0-beta.10", Program.SelectVersion(plugin, null, allowPrerelease: true).Version);
    }

    [TestMethod]
    public void SelectVersion_PicksTheReleaseOverItsOwnPreRelease()
    {
        MarketPlugin plugin = PluginWith("1.0.0-rc.1", "1.0.0", "0.9.9");

        Assert.AreEqual("1.0.0", Program.SelectVersion(plugin, null, allowPrerelease: true).Version);
    }

    [TestMethod]
    public void SelectVersion_NeverPicksAnUnparsableVersionWhileAValidOneExists()
    {
        // 收紧清单校验之前发出去的 1.0.0-beta.01 / 1.0.0-a..b 这类写法:当最旧,不会被挑成「最新」。
        // 2147483648.0.0 没有 '-',旧的「不带 '-' 就是正式版」判法会把它当正式版挑出来。
        MarketPlugin plugin = PluginWith("9.0.0-beta.01", "0.2.0-alpha.1", "9.0.0-a..b", "2147483648.0.0", "nightly");

        Assert.AreEqual("0.2.0-alpha.1", Program.SelectVersion(plugin, null, allowPrerelease: false).Version);
        Assert.AreEqual("0.2.0-alpha.1", Program.SelectVersion(plugin, null, allowPrerelease: true).Version);
    }

    [TestMethod]
    public void SelectVersion_OnlyUnparsableVersions_StillReturnsOne()
    {
        // 排序不该因为版本号写得怪就炸掉整条命令。
        MarketPlugin plugin = PluginWith("nightly", "also-weird");

        Assert.IsNotNull(Program.SelectVersion(plugin, null, allowPrerelease: false));
    }

    [TestMethod]
    [DataRow("2.1.2", "2.1.3", true)]
    [DataRow("2.1.3", "2.1.3", false)]
    [DataRow("2.10.0", "2.9.0", false)]
    [DataRow("1.1.0-beta.1", "1.1.0", false)] // 装载闸忽略预发布:预览版宿主要能装上为它写的插件
    [DataRow("1.2.3", "1.2.3.0", false)]
    [DataRow("1.2.3", "1.2.3.1", true)]
    [DataRow("(unknown)", "1.0.0", false)] // 解析不出就不判低
    [DataRow("1.0.0", "1.0.0-a..b", false)]
    public void IsOlder_IsTheLoadGate(string actual, string required, bool expected) =>
        Assert.AreEqual(expected, Program.IsOlder(actual, required));

    [TestMethod]
    [DataRow("1.2.0", "1.2.0-beta.1", true)] // 装着 beta,正式版出来了就该升
    [DataRow("1.2.0-beta.10", "1.2.0-beta.9", true)]
    [DataRow("1.2.0-beta.1", "1.2.0", false)]
    [DataRow("1.2.0", "1.2.0", false)]
    [DataRow("1.2.0", "1.10.0", false)]
    public void IsNewer_UsesFullPrecedence(string candidate, string installed, bool expected) =>
        Assert.AreEqual(expected, Program.IsNewer(candidate, installed));

    [TestMethod]
    [DataRow("1.1.0", "1.0.0-beta.01")]
    [DataRow("1.0.0-a..b", "0.9.0")]
    public void IsNewer_CannotCompareAnUnparsableVersion(string candidate, string installed) =>
        Assert.IsNull(Program.IsNewer(candidate, installed));
}
