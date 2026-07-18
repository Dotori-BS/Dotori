using Dotori.PackageManager;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Dotori.Tests.PackageManager;

[TestClass]
public sealed class PackageInstallerTests
{
    [TestMethod]
    public async Task InstallFromTarballAsync_DownloadsVerifiesAndPromotesPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var archive = CreateTarball(("package/package.json", "{\"name\":\"demo\"}"));
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(archive));
        using var http = new HttpClient(new StaticResponseHandler(archive));
        try
        {
            var installed = await PackageInstaller.InstallFromTarballAsync(
                Path.Combine(root, "deps"), "demo", "1.0.0", "https://registry/demo.tgz",
                token: null, integrity, httpClient: http);

            Assert.IsTrue(File.Exists(Path.Combine(installed, "package.json")));
            Assert.IsFalse(Directory.EnumerateDirectories(
                Path.Combine(root, "deps", ".staging")).Any());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstallFromTarballAsync_GlobalCacheSupportsOfflineSecondProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var archive = CreateTarball(("package/package.json", "{\"name\":\"cached\"}"));
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(archive));
        var handler = new StaticResponseHandler(archive);
        using var http = new HttpClient(handler);
        var cacheDir = Path.Combine(root, "global-cache");
        try
        {
            await PackageInstaller.InstallFromTarballAsync(
                Path.Combine(root, "first", "deps"), "cached", "1.0.0",
                "https://registry/cached.tgz", null, integrity,
                httpClient: http, cacheDir: cacheDir);
            await PackageInstaller.InstallFromTarballAsync(
                Path.Combine(root, "second", "deps"), "cached", "1.0.0",
                "https://registry/cached.tgz", null, integrity,
                httpClient: http, offline: true, cacheDir: cacheDir);

            Assert.AreEqual(1, handler.RequestCount);
            Assert.IsTrue(File.Exists(Path.Combine(
                root, "second", "deps", "cached", "package.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstallFromTarballAsync_OfflineCacheMissFailsWithoutNetwork()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var archive = CreateTarball(("package/package.json", "{}"));
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(archive));
        var handler = new StaticResponseHandler(archive);
        using var http = new HttpClient(handler);
        try
        {
            await Assert.ThrowsAsync<PackageManagerException>(() =>
                PackageInstaller.InstallFromTarballAsync(
                    Path.Combine(root, "deps"), "missing", "1.0.0",
                    "https://registry/missing.tgz", null, integrity,
                    httpClient: http, offline: true,
                    cacheDir: Path.Combine(root, "empty-cache")));
            Assert.AreEqual(0, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstallFromTarballAsync_DoesNotExtractTraversalEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var archive = CreateTarball(
            ("package/package.json", "{\"name\":\"demo\"}"),
            ("package/../../outside.txt", "escaped"));
        using var http = new HttpClient(new StaticResponseHandler(archive));
        try
        {
            await PackageInstaller.InstallFromTarballAsync(
                Path.Combine(root, "deps"), "demo", "1.0.0", "https://registry/demo.tgz",
                token: null, integrity: null, httpClient: http);

            Assert.IsFalse(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstallFromTarballAsync_IntegrityFailureLeavesNoInstalledPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotori-test-{Guid.NewGuid():N}");
        var archive = CreateTarball(("package/package.json", "{}"));
        using var http = new HttpClient(new StaticResponseHandler(archive));
        try
        {
            await Assert.ThrowsExactlyAsync<PackageManagerException>(() =>
                PackageInstaller.InstallFromTarballAsync(
                    Path.Combine(root, "deps"), "demo", "1.0.0", "https://registry/demo.tgz",
                    token: null, integrity: "sha512-AAAA", httpClient: http));

            Assert.IsFalse(PackageInstaller.IsInstalled(Path.Combine(root, "deps"), "demo"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
    public async Task PackAsync_IsByteReproducibleAcrossOrderAndTimestamps()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotori-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "include"));
        var first = Path.Combine(root, "include", "a.hpp");
        var second = Path.Combine(root, "z.cpp");
        await File.WriteAllTextAsync(first, "#pragma once\n");
        await File.WriteAllTextAsync(second, "int z = 1;\n");
        var archiveA = Path.Combine(root, "first.tgz");
        var archiveB = Path.Combine(root, "second.tgz");
        try
        {
            await PackageInstaller.PackAsync(root, archiveA, [second, first]);
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddYears(-10));
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddYears(10));
            await PackageInstaller.PackAsync(root, archiveB, [first, second]);

            CollectionAssert.AreEqual(
                await File.ReadAllBytesAsync(archiveA),
                await File.ReadAllBytesAsync(archiveB));
            Assert.AreEqual(
                await PackageInstaller.ComputeHashAsync(archiveA),
                await PackageInstaller.ComputeHashAsync(archiveB));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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

    private static byte[] CreateTarball(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                };
                tar.WriteEntry(entry);
            }
        }
        return output.ToArray();
    }

    private sealed class StaticResponseHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }
}
