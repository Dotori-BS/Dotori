using Dotori.PackageManager;

namespace Dotori.Tests.PackageManager;

[TestClass]
public sealed class PackageInstallerTests
{
    [TestMethod]
    public void ScopedNameToPath_RejectsTraversalSegments()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => PackageInstaller.ScopedNameToPath("../outside"));
    }

    [TestMethod]
    public async Task VerifyIntegrityAsync_AcceptsMatchingSha256()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "dotori integrity test");
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var integrity = "sha256-" + Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(bytes));

            await PackageInstaller.VerifyIntegrityAsync(path, integrity);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task VerifyIntegrityAsync_RejectsMismatch()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "dotori integrity test");
        try
        {
            await Assert.ThrowsExactlyAsync<PackageManagerException>(
                () => PackageInstaller.VerifyIntegrityAsync(path, "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void GetLocalDepDir_ReturnsCorrectPath()
    {
        var depsRoot = "/proj/deps";
        var result   = PackageInstaller.GetLocalDepDir(depsRoot, "spdlog");
        Assert.AreEqual(Path.Combine(depsRoot, "spdlog"), result);
    }

    [TestMethod]
    public void IsInstalled_ReturnsFalse_WhenDirMissing()
    {
        var depsRoot = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}", "deps");
        Assert.IsFalse(PackageInstaller.IsInstalled(depsRoot, "spdlog"),
            "Should return false when deps/spdlog/ does not exist");
    }

    [TestMethod]
    public void IsInstalled_ReturnsFalse_WhenDirExistsButNoDotoriFile()
    {
        var tmp      = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var depsRoot = Path.Combine(tmp, "deps");
        Directory.CreateDirectory(Path.Combine(depsRoot, "spdlog"));

        try
        {
            Assert.IsFalse(PackageInstaller.IsInstalled(depsRoot, "spdlog"),
                "Should return false when deps/spdlog/.dotori is absent");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [TestMethod]
    public void IsInstalled_ReturnsTrue_WhenPackageJsonPresent()
    {
        var tmp      = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var depsRoot = Path.Combine(tmp, "deps");
        // @gabime/spdlog → "gabime/spdlog" path
        var pkgDir   = Path.Combine(depsRoot, "gabime", "spdlog");
        Directory.CreateDirectory(pkgDir);
        File.WriteAllText(Path.Combine(pkgDir, "package.json"),
            """{"name":"@gabime/spdlog","version":"1.13.0"}""");

        try
        {
            Assert.IsTrue(PackageInstaller.IsInstalled(depsRoot, "@gabime/spdlog"),
                "Should return true when deps/gabime/spdlog/package.json exists");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
