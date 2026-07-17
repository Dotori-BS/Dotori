using System.Text.Json;
using Dotori.Core;
using Dotori.PackageManager;

namespace Dotori.Tests.PackageManager;

[TestClass]
public sealed class LockManagerTests
{
    // ─── JSON serialization (v2) ─────────────────────────────────────────────

    [TestMethod]
    public void Save_WritesJsonWithLockVersion2()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var lockFile = new LockFile { Name = "@myorg/app", Version = "1.0.0" };
            LockManager.Save(lockFile, tempDir);

            var path = Path.Combine(tempDir, DotoriConstants.LockJsonFileName);
            Assert.IsTrue(File.Exists(path));
            var json = File.ReadAllText(path);
            StringAssert.Contains(json, "\"lockVersion\"");
            StringAssert.Contains(json, "2");
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void SaveAndLoad_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var lockFile = new LockFile { Name = "@myorg/app", Version = "1.0.0" };
            lockFile.Packages["@fmtlib/fmt@10.2.0"] = new LockEntry
            {
                Version   = "10.2.0",
                Resolved  = "https://npm.pkg.github.com/@fmtlib/fmt/-/fmt-10.2.0.tgz",
                Integrity = "sha512-abc123",
                Registry  = "https://npm.pkg.github.com",
                Dependencies = new() { ["@other/dep"] = "^1.0.0" },
            };

            LockManager.Save(lockFile, tempDir);
            var loaded = LockManager.Load(tempDir);

            Assert.AreEqual(2, loaded.LockVersion);
            Assert.AreEqual("@myorg/app", loaded.Name);
            Assert.AreEqual("1.0.0", loaded.Version);
            Assert.AreEqual(1, loaded.Packages.Count);
            Assert.IsTrue(loaded.Packages.ContainsKey("@fmtlib/fmt@10.2.0"));

            var entry = loaded.Packages["@fmtlib/fmt@10.2.0"];
            Assert.AreEqual("10.2.0", entry.Version);
            Assert.AreEqual("sha512-abc123", entry.Integrity);
            Assert.AreEqual("https://npm.pkg.github.com", entry.Registry);
            Assert.AreEqual(1, entry.Dependencies.Count);
            Assert.AreEqual("^1.0.0", entry.Dependencies["@other/dep"]);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void SaveAndLoad_PathDep_LinkFlagPreserved()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var lockFile = new LockFile();
            lockFile.Packages["@myorg/local@1.0.0"] = new LockEntry
            {
                Version  = "1.0.0",
                Resolved = "path:../local",
                Link     = true,
            };

            LockManager.Save(lockFile, tempDir);
            var loaded = LockManager.Load(tempDir);

            var entry = loaded.Packages["@myorg/local@1.0.0"];
            Assert.IsTrue(entry.Link);
            Assert.AreEqual("path:../local", entry.Resolved);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var loaded = LockManager.Load(tempDir);
            Assert.AreEqual(0, loaded.Packages.Count);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void ContentEquals_IgnoresDictionaryInsertionOrder()
    {
        var first = new LockFile { Name = "app", Version = "1.0.0" };
        var second = new LockFile { Name = "app", Version = "1.0.0" };
        first.Packages["a@1.0.0"] = new LockEntry
        {
            Version = "1.0.0", Resolved = "https://example/a.tgz",
            Dependencies = new() { ["b"] = "^2", ["c"] = "^3" },
        };
        second.Packages["a@1.0.0"] = new LockEntry
        {
            Version = "1.0.0", Resolved = "https://example/a.tgz",
            Dependencies = new() { ["c"] = "^3", ["b"] = "^2" },
        };

        Assert.IsTrue(LockManager.ContentEquals(first, second));
        second.Packages["a@1.0.0"].Dependencies["b"] = "^4";
        Assert.IsFalse(LockManager.ContentEquals(first, second));
    }

    // ─── Legacy v1 migration ─────────────────────────────────────────────────

    [TestMethod]
    public void Load_LegacyTomlLock_MigratesAutomatically()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Write legacy v1 lock file
            var toml = """
                lock-version = 1

                [[package]]
                name    = "fmt"
                version = "10.2.0"
                source  = "git+https://github.com/fmtlib/fmt#v10.2.0"
                hash    = "sha256:abcdef"
                deps    = ["spdlog@1.13.0"]
                """;
            File.WriteAllText(Path.Combine(tempDir, DotoriConstants.LockFileName), toml);

            var loaded = LockManager.Load(tempDir);

            Assert.AreEqual(1, loaded.Packages.Count);
            Assert.IsTrue(loaded.Packages.ContainsKey("fmt@10.2.0"));

            var entry = loaded.Packages["fmt@10.2.0"];
            Assert.AreEqual("10.2.0", entry.Version);
            Assert.AreEqual("git+https://github.com/fmtlib/fmt#v10.2.0", entry.Resolved);
            Assert.AreEqual("sha256:abcdef", entry.Integrity);
            Assert.IsTrue(entry.Dependencies.ContainsKey("spdlog"));
            Assert.AreEqual("1.13.0", entry.Dependencies["spdlog"]);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void MigrateFromLegacy_MultiplePackages_AllMigrated()
    {
        var toml = """
            lock-version = 1

            [[package]]
            name    = "fmt"
            version = "10.2.0"
            source  = "git+https://github.com/fmtlib/fmt#v10.2.0"

            [[package]]
            name    = "spdlog"
            version = "1.13.0"
            source  = "git+https://github.com/gabime/spdlog#v1.13.0"
            deps    = ["fmt@10.2.0"]
            """;

        var migrated = LockManager.MigrateFromLegacy(toml);

        Assert.AreEqual(2, migrated.Packages.Count);
        Assert.IsTrue(migrated.Packages.ContainsKey("fmt@10.2.0"));
        Assert.IsTrue(migrated.Packages.ContainsKey("spdlog@1.13.0"));
        Assert.IsTrue(migrated.Packages["spdlog@1.13.0"].Dependencies.ContainsKey("fmt"));
    }

    // ─── npm registry entry ──────────────────────────────────────────────────

    [TestMethod]
    public void SaveAndLoad_ScopedPackageKey_PreservesAtSign()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var lockFile = new LockFile();
            lockFile.Packages["@myorg/utils@2.1.0"] = new LockEntry
            {
                Version  = "2.1.0",
                Resolved = "https://npm.pkg.github.com/@myorg/utils/-/utils-2.1.0.tgz",
                Registry = "https://npm.pkg.github.com",
            };

            LockManager.Save(lockFile, tempDir);
            var loaded = LockManager.Load(tempDir);

            Assert.IsTrue(loaded.Packages.ContainsKey("@myorg/utils@2.1.0"));
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
