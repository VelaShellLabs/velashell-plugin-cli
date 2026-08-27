using VelaShell.Plugin.Cli;

namespace VelaShell.Plugin.Cli.Tests;

/// <summary>
/// 商店地址的取值。这个值决定 <c>install</c> 去哪里要包,所以它既要能被自建商店覆盖,
/// 又不能被覆盖成一个不该发请求的地方。
/// </summary>
[TestClass]
[TestCategory("Plugins")]
public class MarketplaceTests
{
    private string? _saved;

    [TestInitialize]
    public void Setup()
    {
        _saved = Environment.GetEnvironmentVariable(Marketplace.BaseUrlEnvironmentVariable);
        Environment.SetEnvironmentVariable(Marketplace.BaseUrlEnvironmentVariable, null);
    }

    [TestCleanup]
    public void Cleanup() =>
        Environment.SetEnvironmentVariable(Marketplace.BaseUrlEnvironmentVariable, _saved);

    [TestMethod]
    public void ResolveBaseUrl_DefaultsToTheOfficialMarketplace() =>
        Assert.AreEqual(Marketplace.DefaultBaseUrl, Marketplace.ResolveBaseUrl(null));

    [TestMethod]
    public void ResolveBaseUrl_PrefersTheOptionOverTheEnvironment()
    {
        Environment.SetEnvironmentVariable(Marketplace.BaseUrlEnvironmentVariable, "https://from-env.example");

        Assert.AreEqual("https://from-env.example", Marketplace.ResolveBaseUrl(null));
        Assert.AreEqual("https://from-option.example", Marketplace.ResolveBaseUrl("https://from-option.example"));
    }

    [TestMethod]
    public void ResolveBaseUrl_DropsTheTrailingSlash() =>
        // 后面所有路径都以 / 开头,不去掉就会拼出 //api/plugins。
        Assert.AreEqual("https://market.example", Marketplace.ResolveBaseUrl("https://market.example/"));

    [TestMethod]
    [DataRow("file:///etc/passwd")]
    [DataRow("ftp://market.example")]
    [DataRow("market.example")]
    [DataRow("")]
    public void ResolveBaseUrl_RefusesAnythingThatIsNotAnAbsoluteHttpUrl(string value) =>
        Assert.ThrowsExactly<CliException>(() => Marketplace.ResolveBaseUrl(value));

    [TestMethod]
    public void ResolveBaseUrl_AllowsPlainHttp() =>
        // 自建商店跑在内网 http 上是常见的;下载环节会另行提醒,这里不拦。
        Assert.AreEqual("http://market.internal:8080", Marketplace.ResolveBaseUrl("http://market.internal:8080"));
}
