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
        StringAssert.Contains(error.Message, "1.4.1", StringComparison.Ordinal);
    }

    [TestMethod]
    public void SelectVersion_NoPublishedVersions_SaysSo()
    {
        CliException error = Assert.ThrowsExactly<CliException>(
            () => Program.SelectVersion(PluginWith(), null, allowPrerelease: false));

        StringAssert.Contains(error.Message, "no published versions", StringComparison.Ordinal);
    }

    [TestMethod]
    public void VersionOrder_SortsAPreReleaseBelowItsOwnRelease()
    {
        string[] sorted = ["1.0.0", "1.0.0-rc.1", "0.9.9"];
        Array.Sort(sorted, VersionOrder.Instance);

        CollectionAssert.AreEqual(new[] { "0.9.9", "1.0.0-rc.1", "1.0.0" }, sorted);
    }

    [TestMethod]
    public void VersionOrder_DoesNotThrowOnVersionsItCannotParse()
    {
        // 商店上出现一个奇怪版本号时,排序退化成序数比较即可,不该让整条命令炸掉。
        string[] sorted = ["nightly", "1.0.0", "also-weird"];
        Array.Sort(sorted, VersionOrder.Instance);

        Assert.AreEqual(3, sorted.Length);
    }
}
