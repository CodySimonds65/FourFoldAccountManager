using System.Net;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginNetworkPolicyTests
{
    private static PluginManifest Manifest(bool anySite = false) => new(
        "cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1, "index.html", null,
        [new Uri("https://wiki.example.com"), new Uri("https://api.example.com:8443")], anySite, []);

    [Theory]
    [InlineData("https://cody--goal-tracker.fourfoldplugin/app.js", true)]
    [InlineData("https://wiki.example.com/page?x=1", true)]
    [InlineData("https://WIKI.example.com/page", true)]
    [InlineData("https://api.example.com:8443/v1", true)]
    [InlineData("https://api.example.com/v1", false)]
    [InlineData("https://other.example.com/", false)]
    [InlineData("https://evil-wiki.example.com.attacker.net/", false)]
    [InlineData("http://wiki.example.com/page", false)]
    [InlineData("https://other--plugin.fourfoldplugin/app.js", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void ADefaultPluginReachesOnlyItsOwnFilesAndDeclaredSites(string url, bool allowed) =>
        Assert.Equal(allowed, PluginNetworkPolicy.IsAllowed(new Uri(url), Manifest(), PluginTrust.Developer));

    [Theory]
    [InlineData("https://localhost/")]
    [InlineData("https://app.localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.1.2.3/")]
    [InlineData("https://172.16.0.9/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://169.254.10.10/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[fd00::1]/")]
    [InlineData("https://[fe80::1]/")]
    [InlineData("https://[::ffff:192.168.1.1]/")]
    [InlineData("https://localhost./")]
    [InlineData("https://127.0.0.1./")]
    [InlineData("https://192.168.1.1./")]
    [InlineData("https://100.64.1.1/")]
    [InlineData("https://[::127.0.0.1]/")]
    [InlineData("https://[64:ff9b::7f00:1]/")]
    [InlineData("https://[2002:7f00:1::]/")]
    public void TheLocalNetworkIsNeverReachableEvenWithAnySite(string url) =>
        Assert.False(PluginNetworkPolicy.IsAllowed(new Uri(url), Manifest(anySite: true), PluginTrust.Verified));

    [Fact]
    public void ThePluginNamespaceIsNeverAnOrdinarySite()
    {
        var manifestWithPluginHost = new PluginManifest(
            "cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1, "index.html", null,
            [new Uri("https://wiki.example.com"), new Uri("https://other--plugin.fourfoldplugin")], true, []);

        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("https://other--plugin.fourfoldplugin/app.js"), manifestWithPluginHost, PluginTrust.Verified));
        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("https://fourfoldplugin/"), manifestWithPluginHost, PluginTrust.Verified));
        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("https://x.y.fourfoldplugin/"), manifestWithPluginHost, PluginTrust.Verified));
        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("https://cody--goal-tracker.fourfoldplugin./"), manifestWithPluginHost, PluginTrust.Verified));
        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("https://cody--goal-tracker.fourfoldplugin:8443/"), manifestWithPluginHost, PluginTrust.Verified));
        Assert.True(PluginNetworkPolicy.IsAllowed(
            new Uri("https://cody--goal-tracker.fourfoldplugin/app.js"), manifestWithPluginHost, PluginTrust.Verified));
    }

    [Fact]
    public void AnySiteNeedsTrustAndNeverAllowsOutsideScriptsOrPlainHttp()
    {
        var any = new Uri("https://anything.example.org/data.json");

        Assert.True(PluginNetworkPolicy.IsAllowed(any, Manifest(anySite: true), PluginTrust.Verified));
        Assert.True(PluginNetworkPolicy.IsAllowed(any, Manifest(anySite: true), PluginTrust.Developer));
        Assert.False(PluginNetworkPolicy.IsAllowed(any, Manifest(anySite: true), PluginTrust.Standard));
        Assert.False(PluginNetworkPolicy.IsAllowed(any, Manifest(anySite: true), PluginTrust.Verified, isScript: true));
        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("https://wiki.example.com/lib.js"), Manifest(), PluginTrust.Developer, isScript: true));
        Assert.False(PluginNetworkPolicy.IsAllowed(
            new Uri("http://anything.example.org/"), Manifest(anySite: true), PluginTrust.Verified));
    }

    [Fact]
    public void TheContentSecurityPolicyNamesOnlyDeclaredSitesAndNeverAllowsInlineScriptOrEval()
    {
        var policy = PluginNetworkPolicy.BuildContentSecurityPolicy(Manifest(), PluginTrust.Developer);
        Assert.Equal(
            "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https://wiki.example.com https://api.example.com:8443; connect-src 'self' https://wiki.example.com https://api.example.com:8443 wss://wiki.example.com wss://api.example.com:8443; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'",
            policy);

        var anySite = PluginNetworkPolicy.BuildContentSecurityPolicy(Manifest(anySite: true), PluginTrust.Verified);
        Assert.Equal(
            "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https:; connect-src 'self' https: wss://wiki.example.com wss://api.example.com:8443; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'",
            anySite);

        var untrusted = PluginNetworkPolicy.BuildContentSecurityPolicy(Manifest(anySite: true), PluginTrust.Standard);
        Assert.Equal(
            "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https://wiki.example.com https://api.example.com:8443; connect-src 'self' https://wiki.example.com https://api.example.com:8443 wss://wiki.example.com wss://api.example.com:8443; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'",
            untrusted);

        var noSites = new PluginManifest("cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1,
            "index.html", null, [], false, []);
        var nositespolicy = PluginNetworkPolicy.BuildContentSecurityPolicy(noSites, PluginTrust.Developer);
        Assert.Equal(
            "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'",
            nositespolicy);
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.31.255.255", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.64.0.0", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("100.128.0.0", false)]
    public void LocalAddressRangesAreRecognised(string address, bool local) =>
        Assert.Equal(local, PluginNetworkPolicy.IsLocalAddress(IPAddress.Parse(address)));

    [Fact]
    public void Ipv6EmbeddedAddressesAreLocal()
    {
        Assert.True(PluginNetworkPolicy.IsLocalAddress(IPAddress.Parse("::1")));
        Assert.False(PluginNetworkPolicy.IsLocalAddress(IPAddress.Parse("2001:4860:4860::8888")));
    }
}
