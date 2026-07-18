using Dotori.Core.Build;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using System.Text.Json;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class InstallManagerTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [TestMethod]
    public void InstallAndUninstall_CopiesArtifactAndPublicHeadersFromManifest()
    {
        var include = Path.Combine(_tempDir, "include", "sample");
        Directory.CreateDirectory(include);
        File.WriteAllText(Path.Combine(include, "api.hpp"), "#pragma once");
        var artifact = Path.Combine(_tempDir, "libsample.a");
        File.WriteAllText(artifact, "archive");
        var model = MakeModel(ProjectType.StaticLibrary);
        model.Headers.Add(new HeaderItem(true, "include"));
        model.Headers.Add(new HeaderItem(false, "private"));
        var prefix = Path.Combine(_tempDir, "prefix");

        var installed = InstallManager.Install(
            model, artifact, prefix, null, "linux-x64", "release");

        Assert.HasCount(2, installed);
        Assert.IsTrue(File.Exists(Path.Combine(prefix, "lib", "libsample.a")));
        Assert.IsTrue(File.Exists(Path.Combine(prefix, "include", "sample", "api.hpp")));
        Assert.IsTrue(File.Exists(InstallManager.GetManifestPath(
            prefix, "sample", "linux-x64", "release")));

        var removed = InstallManager.Uninstall(
            "sample", prefix, null, "linux-x64", "release");
        Assert.HasCount(2, removed);
        Assert.IsFalse(File.Exists(Path.Combine(prefix, "lib", "libsample.a")));
        Assert.IsFalse(File.Exists(Path.Combine(prefix, "include", "sample", "api.hpp")));
    }

    [TestMethod]
    public void Install_ReinstallRemovesFilesNoLongerDeclared()
    {
        var include = Path.Combine(_tempDir, "include");
        Directory.CreateDirectory(include);
        File.WriteAllText(Path.Combine(include, "old.hpp"), "old");
        var model = MakeModel(ProjectType.HeaderOnly);
        model.Headers.Add(new HeaderItem(true, "include"));
        var prefix = Path.Combine(_tempDir, "prefix");
        InstallManager.Install(model, null, prefix, null, "host", "debug");
        model.Headers.Clear();

        InstallManager.Install(model, null, prefix, null, "host", "debug");

        Assert.IsFalse(File.Exists(Path.Combine(prefix, "include", "old.hpp")));
    }

    [TestMethod]
    public void Uninstall_RejectsManifestPathOutsidePrefixBeforeDeletingAnything()
    {
        var artifact = Path.Combine(_tempDir, "app");
        File.WriteAllText(artifact, "binary");
        var model = MakeModel(ProjectType.Executable);
        var prefix = Path.Combine(_tempDir, "prefix");
        InstallManager.Install(model, artifact, prefix, null, "host", "debug");
        var installed = Path.Combine(prefix, "bin", "app");
        var outside = Path.Combine(_tempDir, "outside");
        File.WriteAllText(outside, "keep");
        var manifest = InstallManager.GetManifestPath(prefix, "sample", "host", "debug");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            Project = "sample", Prefix = prefix, Files = new[] { installed, outside },
        }));

        Assert.Throws<InvalidDataException>(() =>
            InstallManager.Uninstall("sample", prefix, null, "host", "debug"));
        Assert.IsTrue(File.Exists(installed));
        Assert.IsTrue(File.Exists(outside));
    }

    [TestMethod]
    public void ResolveEffectivePrefix_AppliesDestDirWithoutChangingLogicalPrefix()
    {
        var stage = Path.Combine(_tempDir, "stage");
        var prefix = Path.Combine(Path.GetPathRoot(_tempDir)!, "usr", "local");

        var effective = InstallManager.ResolveEffectivePrefix(prefix, stage);

        Assert.AreEqual(Path.Combine(stage, "usr", "local"), effective);
    }

    private FlatProjectModel MakeModel(ProjectType type) => new()
    {
        Name = "sample",
        ProjectDir = _tempDir,
        DotoriPath = Path.Combine(_tempDir, "sample.dotori"),
        Type = type,
    };
}
