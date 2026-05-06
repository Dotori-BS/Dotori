using Dotori.PackageManager;
using Dotori.PackageManager.Config;

namespace Dotori.Tests.PackageManager;

[TestClass]
public sealed class NpmRegistryClientTests
{
    // ─── NpmDtos deserialization ─────────────────────────────────────────────

    [TestMethod]
    public void NpmPackument_Deserialize_ParsesVersionsAndDistTags()
    {
        var json = """
        {
          "_id": "@fmtlib/fmt",
          "name": "@fmtlib/fmt",
          "description": "fmt library",
          "dist-tags": { "latest": "10.2.0" },
          "versions": {
            "10.2.0": {
              "name": "@fmtlib/fmt",
              "version": "10.2.0",
              "dist": {
                "tarball": "https://npm.pkg.github.com/@fmtlib/fmt/-/fmt-10.2.0.tgz",
                "integrity": "sha512-abc123",
                "shasum": "aabbcc"
              }
            }
          }
        }
        """;

        var packument = System.Text.Json.JsonSerializer.Deserialize(
            json, NpmDtoContext.Default.NpmPackument);

        Assert.IsNotNull(packument);
        Assert.AreEqual("@fmtlib/fmt", packument.Name);
        Assert.AreEqual("10.2.0", packument.DistTags["latest"]);
        Assert.IsTrue(packument.Versions.ContainsKey("10.2.0"));

        var ver = packument.Versions["10.2.0"];
        Assert.IsNotNull(ver.Dist);
        Assert.AreEqual("sha512-abc123", ver.Dist.Integrity);
        Assert.AreEqual("https://npm.pkg.github.com/@fmtlib/fmt/-/fmt-10.2.0.tgz",
            ver.Dist.Tarball);
    }

    [TestMethod]
    public void NpmVersionManifest_Deserialize_ParsesDependencies()
    {
        var json = """
        {
          "name": "@myorg/utils",
          "version": "2.0.0",
          "dependencies": {
            "@fmtlib/fmt": "^10.0.0",
            "@gabime/spdlog": "~1.13.0"
          }
        }
        """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize(
            json, NpmDtoContext.Default.NpmVersionManifest);

        Assert.IsNotNull(manifest);
        Assert.IsNotNull(manifest.Dependencies);
        Assert.AreEqual(2, manifest.Dependencies.Count);
        Assert.AreEqual("^10.0.0", manifest.Dependencies["@fmtlib/fmt"]);
    }

    // ─── Scope routing (DotoriConfig) ───────────────────────────────────────

    [TestMethod]
    public void ForPackage_ScopedName_RoutesToCorrectRegistry()
    {
        var config = new DotoriConfig
        {
            DefaultRegistry = "https://registry.npmjs.org",
        };
        config.ScopeMap["@myorg"] = "https://npm.pkg.github.com";
        config.Registries["https://npm.pkg.github.com"] = new RegistryConfig
        {
            Url = "https://npm.pkg.github.com",
            Token = "ghp_test",
        };

        Assert.AreEqual("https://npm.pkg.github.com",
            config.ResolveRegistryUrl("@myorg/utils"));
        Assert.AreEqual("https://registry.npmjs.org",
            config.ResolveRegistryUrl("@other/lib"));
    }
}
