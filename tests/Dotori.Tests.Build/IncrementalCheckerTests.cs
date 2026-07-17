using Dotori.Core.Build;

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
}
