using Dotori.PackageManager.Config;

namespace Dotori.Tests.PackageManager;

[TestClass]
public sealed class DotoriConfigManagerTests
{
    // ─── TOML parser ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ParseToml_DefaultRegistry_Parsed()
    {
        var toml = """
            [registry]
            default = "https://npm.pkg.github.com"
            """;

        var config = new DotoriConfig();
        DotoriConfigManager.ParseToml(toml, config);

        Assert.AreEqual("https://npm.pkg.github.com", config.DefaultRegistry);
    }

    [TestMethod]
    public void ParseToml_RegistryTokenAndUpstream_Parsed()
    {
        var toml = """
            [registry]
            default = "https://registry.npmjs.org"

            [registry."https://npm.pkg.github.com"]
            token = "ghp_TOKEN"
            upstream = "https://upstream.example.com"
            """;

        var config = new DotoriConfig();
        DotoriConfigManager.ParseToml(toml, config);

        Assert.IsTrue(config.Registries.ContainsKey("https://npm.pkg.github.com"));
        var reg = config.Registries["https://npm.pkg.github.com"];
        Assert.AreEqual("ghp_TOKEN", reg.Token);
        Assert.AreEqual("https://upstream.example.com", reg.Upstream);
    }

    [TestMethod]
    public void ParseToml_Scopes_Parsed()
    {
        var toml = """
            [scopes]
            "@myorg" = "https://npm.pkg.github.com"
            "@private" = "https://jfrog.example.com"
            """;

        var config = new DotoriConfig();
        DotoriConfigManager.ParseToml(toml, config);

        Assert.AreEqual("https://npm.pkg.github.com", config.ScopeMap["@myorg"]);
        Assert.AreEqual("https://jfrog.example.com", config.ScopeMap["@private"]);
    }

    // ─── .npmrc parser ───────────────────────────────────────────────────────

    [TestMethod]
    public void ParseNpmrc_ScopeRegistry_AddedToScopeMap()
    {
        var npmrc = "@myorg:registry=https://npm.pkg.github.com";

        var config = new DotoriConfig();
        DotoriConfigManager.ParseNpmrc(npmrc, config);

        Assert.AreEqual("https://npm.pkg.github.com", config.ScopeMap["@myorg"]);
    }

    [TestMethod]
    public void ParseNpmrc_AuthToken_AddedToRegistries()
    {
        var npmrc = "//npm.pkg.github.com/:_authToken=ghp_TOKEN";

        var config = new DotoriConfig();
        // Pre-populate so the host can be matched
        config.Registries["https://npm.pkg.github.com"] = new RegistryConfig
        {
            Url = "https://npm.pkg.github.com"
        };
        DotoriConfigManager.ParseNpmrc(npmrc, config);

        Assert.AreEqual("ghp_TOKEN",
            config.Registries["https://npm.pkg.github.com"].Token);
    }

    [TestMethod]
    public void ParseNpmrc_DefaultRegistry_UpdatesDefault()
    {
        var npmrc = "registry=https://verdaccio.example.com";

        var config = new DotoriConfig();
        DotoriConfigManager.ParseNpmrc(npmrc, config);

        Assert.AreEqual("https://verdaccio.example.com", config.DefaultRegistry);
    }

    [TestMethod]
    public void ParseNpmrc_MultipleLines_AllParsed()
    {
        var npmrc = """
            @myorg:registry=https://npm.pkg.github.com
            @private:registry=https://jfrog.example.com
            //npm.pkg.github.com/:_authToken=ghp_TOKEN
            """;

        var config = new DotoriConfig();
        config.Registries["https://npm.pkg.github.com"] = new RegistryConfig
        {
            Url = "https://npm.pkg.github.com"
        };
        DotoriConfigManager.ParseNpmrc(npmrc, config);

        Assert.AreEqual(2, config.ScopeMap.Count);
        Assert.AreEqual("ghp_TOKEN",
            config.Registries["https://npm.pkg.github.com"].Token);
    }

    // ─── ResolveRegistryUrl ──────────────────────────────────────────────────

    [TestMethod]
    public void ResolveRegistryUrl_ScopedPackage_ReturnsCorrectRegistry()
    {
        var config = new DotoriConfig
        {
            DefaultRegistry = "https://registry.npmjs.org"
        };
        config.ScopeMap["@myorg"] = "https://npm.pkg.github.com";

        Assert.AreEqual("https://npm.pkg.github.com",
            config.ResolveRegistryUrl("@myorg/lib"));
        Assert.AreEqual("https://registry.npmjs.org",
            config.ResolveRegistryUrl("@other/lib"));
        Assert.AreEqual("https://registry.npmjs.org",
            config.ResolveRegistryUrl("unscoped-lib"));
    }
}
