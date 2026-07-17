using Dotori.Core.Build;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class IncrementalCheckerTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void IsChanged_NewFile_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "foo.cpp");
        File.WriteAllText(file, "int main() {}");

        using var checker = new IncrementalChecker(_tempDir);
        Assert.IsTrue(checker.IsChanged(file));
    }

    [TestMethod]
    public void IsChanged_AfterRecord_ReturnsFalse()
    {
        var file = Path.Combine(_tempDir, "foo.cpp");
        File.WriteAllText(file, "int main() {}");

        using var checker = new IncrementalChecker(_tempDir);
        checker.Record(file);
        Assert.IsFalse(checker.IsChanged(file));
    }

    [TestMethod]
    public void IsChanged_AfterModification_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "foo.cpp");
        File.WriteAllText(file, "int main() {}");

        using var checker = new IncrementalChecker(_tempDir);
        checker.Record(file);
        checker.Save();

        // Modify the file
        File.WriteAllText(file, "int main() { return 1; }");

        using var checker2 = new IncrementalChecker(_tempDir);
        Assert.IsTrue(checker2.IsChanged(file));
    }

    [TestMethod]
    public void IsChanged_NonexistentFile_ReturnsTrue()
    {
        using var checker = new IncrementalChecker(_tempDir);
        Assert.IsTrue(checker.IsChanged("/nonexistent/file.cpp"));
    }

    [TestMethod]
    public void GetFileState_DistinguishesCacheReasons()
    {
        var file = Path.Combine(_tempDir, "state.cpp");
        using var checker = new IncrementalChecker(_tempDir);

        Assert.AreEqual(IncrementalFileState.Missing, checker.GetFileState(file));
        File.WriteAllText(file, "int value = 1;");
        Assert.AreEqual(IncrementalFileState.Untracked, checker.GetFileState(file));
        checker.Record(file);
        Assert.AreEqual(IncrementalFileState.Unchanged, checker.GetFileState(file));
        File.WriteAllText(file, "int value = 2;");
        Assert.AreEqual(IncrementalFileState.Modified, checker.GetFileState(file));
    }

    [TestMethod]
    public void Save_PersistsAcrossInstances()
    {
        var file = Path.Combine(_tempDir, "foo.cpp");
        File.WriteAllText(file, "int main() {}");

        using (var checker = new IncrementalChecker(_tempDir))
        {
            checker.Record(file);
            checker.Save();
        }

        using var checker2 = new IncrementalChecker(_tempDir);
        Assert.IsFalse(checker2.IsChanged(file));
    }

    [TestMethod]
    public void Save_ConcurrentInstances_MergesIndependentUpdates()
    {
        var firstFile = Path.Combine(_tempDir, "first.cpp");
        var secondFile = Path.Combine(_tempDir, "second.cpp");
        File.WriteAllText(firstFile, "int first;");
        File.WriteAllText(secondFile, "int second;");

        using var first = new IncrementalChecker(_tempDir, "debug");
        using var second = new IncrementalChecker(_tempDir, "debug");
        first.Record(firstFile);
        second.Record(secondFile);
        first.Save();
        second.Save();

        using var reloaded = new IncrementalChecker(_tempDir, "debug");
        Assert.IsFalse(reloaded.IsChanged(firstFile));
        Assert.IsFalse(reloaded.IsChanged(secondFile));
        Assert.IsFalse(Directory.GetFiles(
            Path.Combine(_tempDir, ".dotori-cache"), "*.tmp.*").Any());
    }

    [TestMethod]
    public void IsChanged_UsesIndependentTargetAndConfigScopes()
    {
        var file = Path.Combine(_tempDir, "foo.cpp");
        File.WriteAllText(file, "int main() {}");

        using (var debug = new IncrementalChecker(_tempDir, "macos-arm64-debug"))
        {
            debug.Record(file);
            debug.Save();
        }

        using var debugAgain = new IncrementalChecker(_tempDir, "macos-arm64-debug");
        using var release = new IncrementalChecker(_tempDir, "macos-arm64-release");
        Assert.IsFalse(debugAgain.IsChanged(file));
        Assert.IsTrue(release.IsChanged(file));
    }

    [TestMethod]
    public void PlanCompileJobs_MissingObject_RebuildsUnchangedSource()
    {
        var source = Path.Combine(_tempDir, "main.cpp");
        File.WriteAllText(source, "int main() { return 0; }");
        var model = new FlatProjectModel
        {
            Name = "app", ProjectDir = _tempDir,
            DotoriPath = Path.Combine(_tempDir, ".dotori"),
        };
        model.Sources.Add(new SourceItem(true, "main.cpp"));
        var toolchain = new ToolchainInfo
        {
            Kind = CompilerKind.Clang, CompilerPath = "clang++", LinkerPath = "clang++",
            TargetTriple = "x86_64-unknown-linux-gnu",
        };
        var planner = new BuildPlanner(model, toolchain, "debug", "linux-x64");

        using var checker = new IncrementalChecker(_tempDir, "linux-x64-debug");
        checker.Record(source);
        IReadOnlyList<string>? reasons = null;
        var missingOutputJobs = planner.PlanCompileJobs(checker,
            explain: (_, reported) => reasons = reported);
        Assert.HasCount(1, missingOutputJobs);
        CollectionAssert.Contains(reasons!.ToArray(), "output is missing");

        File.WriteAllText(missingOutputJobs[0].OutputFile, "object");
        checker.RecordFingerprint(
            missingOutputJobs[0].OutputFile, missingOutputJobs[0].Fingerprint);
        Assert.HasCount(0, planner.PlanCompileJobs(checker));
    }

    [TestMethod]
    public void PlanCompileJobs_TransitiveHeaderChanged_RebuildsSource()
    {
        var includeDir = Path.Combine(_tempDir, "include");
        Directory.CreateDirectory(includeDir);
        var leaf = Path.Combine(includeDir, "leaf.h");
        var header = Path.Combine(includeDir, "library.h");
        var source = Path.Combine(_tempDir, "main.cpp");
        File.WriteAllText(leaf, "constexpr int value = 1;");
        File.WriteAllText(header, "#include \"leaf.h\"");
        File.WriteAllText(source, "#include \"library.h\"\nint main() { return value; }");

        var model = new FlatProjectModel
        {
            Name = "app", ProjectDir = _tempDir,
            DotoriPath = Path.Combine(_tempDir, ".dotori"),
        };
        model.Sources.Add(new SourceItem(true, "main.cpp"));
        model.Headers.Add(new HeaderItem(isPublic: false, path: includeDir));
        var toolchain = new ToolchainInfo
        {
            Kind = CompilerKind.Clang, CompilerPath = "clang++", LinkerPath = "clang++",
            TargetTriple = "x86_64-unknown-linux-gnu",
        };
        var planner = new BuildPlanner(model, toolchain, "debug", "linux-x64");

        using var checker = new IncrementalChecker(_tempDir, "linux-x64-debug");
        var initialJob = planner.PlanCompileJobs()[0];
        File.WriteAllText(initialJob.OutputFile, "object");
        checker.Record(source);
        foreach (var dependency in initialJob.DependencyFiles) checker.Record(dependency);
        checker.RecordFingerprint(initialJob.OutputFile, initialJob.Fingerprint);
        Assert.HasCount(0, planner.PlanCompileJobs(checker));

        File.WriteAllText(leaf, "constexpr int value = 2;");
        Assert.HasCount(1, planner.PlanCompileJobs(checker));
    }

    [TestMethod]
    public void PlanCompileJobs_TrackedHeaderRemoved_RebuildsSource()
    {
        var header = Path.Combine(_tempDir, "library.h");
        var source = Path.Combine(_tempDir, "main.cpp");
        File.WriteAllText(header, "constexpr int value = 1;");
        File.WriteAllText(source, "#include \"library.h\"\nint main() { return value; }");
        var model = new FlatProjectModel
        {
            Name = "app", ProjectDir = _tempDir,
            DotoriPath = Path.Combine(_tempDir, ".dotori"),
        };
        model.Sources.Add(new SourceItem(true, "main.cpp"));
        var toolchain = new ToolchainInfo
        {
            Kind = CompilerKind.Clang, CompilerPath = "missing-test-clang++",
            LinkerPath = "missing-test-clang++", TargetTriple = "x86_64-unknown-linux-gnu",
        };
        var planner = new BuildPlanner(model, toolchain, "debug", "linux-x64");
        using var checker = new IncrementalChecker(_tempDir, "linux-x64-debug");
        var initial = planner.PlanCompileJobs()[0];
        File.WriteAllText(initial.OutputFile, "object");
        checker.Record(source);
        checker.Record(header);
        checker.RecordFingerprint(initial.OutputFile, initial.Fingerprint);
        Assert.HasCount(0, planner.PlanCompileJobs(checker));

        File.Delete(header);
        Assert.HasCount(1, planner.PlanCompileJobs(checker));
    }

    [TestMethod]
    public void PlanCompileJobs_CompileFlagsChanged_RebuildsSource()
    {
        var source = Path.Combine(_tempDir, "main.cpp");
        File.WriteAllText(source, "int main() { return 0; }");
        var model = new FlatProjectModel
        {
            Name = "app", ProjectDir = _tempDir,
            DotoriPath = Path.Combine(_tempDir, ".dotori"),
        };
        model.Sources.Add(new SourceItem(true, "main.cpp"));
        var toolchain = new ToolchainInfo
        {
            Kind = CompilerKind.Clang, CompilerPath = "missing-test-clang++",
            LinkerPath = "missing-test-clang++", TargetTriple = "x86_64-unknown-linux-gnu",
        };

        using var checker = new IncrementalChecker(_tempDir, "linux-x64-debug");
        var initialPlanner = new BuildPlanner(model, toolchain, "debug", "linux-x64");
        var initialJob = initialPlanner.PlanCompileJobs()[0];
        File.WriteAllText(initialJob.OutputFile, "object");
        checker.Record(source);
        checker.RecordFingerprint(initialJob.OutputFile, initialJob.Fingerprint);
        Assert.HasCount(0, initialPlanner.PlanCompileJobs(checker));

        model.Defines.Add("FEATURE_ENABLED=1");
        var changedPlanner = new BuildPlanner(model, toolchain, "debug", "linux-x64");
        Assert.HasCount(1, changedPlanner.PlanCompileJobs(checker));
    }

    [TestMethod]
    public void PlanLinkJob_LinkFlagsChanged_ChangesFingerprint()
    {
        var model = new FlatProjectModel
        {
            Name = "app", ProjectDir = _tempDir,
            DotoriPath = Path.Combine(_tempDir, ".dotori"),
        };
        var toolchain = new ToolchainInfo
        {
            Kind = CompilerKind.Clang, CompilerPath = "missing-test-clang++",
            LinkerPath = "missing-test-clang++", TargetTriple = "x86_64-unknown-linux-gnu",
        };
        using var checker = new IncrementalChecker(_tempDir, "linux-x64-debug");

        var initial = new BuildPlanner(model, toolchain, "debug", "linux-x64")
            .PlanLinkJob(["main.o"])!;
        checker.RecordFingerprint(initial.OutputFile, initial.Fingerprint);
        Assert.IsFalse(checker.IsFingerprintChanged(initial.OutputFile, initial.Fingerprint));

        model.LinkFlags.Add("-Wl,--gc-sections");
        var changed = new BuildPlanner(model, toolchain, "debug", "linux-x64")
            .PlanLinkJob(["main.o"])!;
        Assert.IsTrue(checker.IsFingerprintChanged(changed.OutputFile, changed.Fingerprint));
    }

    [TestMethod]
    public void SafeCleanDirectory_RejectsProjectAndOutsidePaths()
    {
        var inside = Path.Combine(_tempDir, "build", "output");
        var outside = Path.Combine(Path.GetDirectoryName(_tempDir)!, "shared-output");

        Assert.IsTrue(PathUtils.IsSafeCleanDirectory(_tempDir, inside));
        Assert.IsFalse(PathUtils.IsSafeCleanDirectory(_tempDir, _tempDir));
        Assert.IsFalse(PathUtils.IsSafeCleanDirectory(_tempDir, outside));
    }
}
