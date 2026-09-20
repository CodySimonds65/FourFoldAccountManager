using FourFoldAccountManager.Core.Navigation;

namespace FourFoldAccountManager.Core.Tests;

[TestClass]
public sealed class FourFoldNavigationPolicyTests
{
    private readonly FourFoldNavigationPolicy _policy = new(["fourfoldonline.com"]);

    [TestMethod]
    public void TryValidate_AllowsHttpsOnConfiguredHost()
    {
        var accepted = _policy.TryValidate(new Uri("https://fourfoldonline.com/login.php"), out var approved);

        Assert.IsTrue(accepted);
        Assert.AreEqual("fourfoldonline.com", approved.IdnHost);
    }

    [TestMethod]
    public void TryValidate_IgnoresHostCasingAndExplicitDefaultPort()
    {
        var accepted = _policy.TryValidate(new Uri("https://FOURFOLDONLINE.COM:443/play.php"), out var approved);

        Assert.IsTrue(accepted);
        Assert.AreEqual("https", approved.Scheme);
    }

    [TestMethod]
    [DataRow("http://fourfoldonline.com/")]
    [DataRow("javascript:alert(1)")]
    [DataRow("https://www.fourfoldonline.com/")]
    [DataRow("https://fourfoldonline.com.attacker.test/")]
    [DataRow("https://user:pass@fourfoldonline.com/")]
    [DataRow("https://fourfoldonline.com:444/")]
    public void TryValidate_RejectsUnsafeOrUnconfiguredUri(string value)
    {
        var accepted = _policy.TryValidate(new Uri(value), out _);

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public void Constructor_WhenAllowedHostIsNotAHostname_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FourFoldNavigationPolicy(["https://fourfoldonline.com"]));
    }
}
