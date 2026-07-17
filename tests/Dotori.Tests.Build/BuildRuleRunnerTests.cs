using Dotori.Core.Build;
using Dotori.Core.Model;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class BuildRuleRunnerTests
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
    public async Task RunAsync_ExecutesOnlyWhenInputChanges()
    {
        var input = Path.Combine(_tempDir, "input.txt");
        var output = Path.Combine(_tempDir, "generated", "output.txt");
        File.WriteAllText(input, "first");
        var command = OperatingSystem.IsWindows()
            ? "copy /Y input.txt generated\\output.txt"
            : "cp input.txt generated/output.txt";
        var rule = new BuildRuleConfig("copy", command, ["input.txt"], ["generated/output.txt"]);
        using var checker = new IncrementalChecker(_tempDir, "rules");

        Assert.AreEqual(0, await BuildRuleRunner.RunAsync(
            [rule], _tempDir, "host", "debug", _tempDir, checker));
        Assert.AreEqual("first", File.ReadAllText(output));

        File.WriteAllText(output, "sentinel");
        Assert.AreEqual(0, await BuildRuleRunner.RunAsync(
            [rule], _tempDir, "host", "debug", _tempDir, checker));
        Assert.AreEqual("sentinel", File.ReadAllText(output));

        File.WriteAllText(input, "second");
        Assert.AreEqual(0, await BuildRuleRunner.RunAsync(
            [rule], _tempDir, "host", "debug", _tempDir, checker));
        Assert.AreEqual("second", File.ReadAllText(output));
    }

    [TestMethod]
    public async Task RunAsync_DryRunDoesNotCreateOutput()
    {
        File.WriteAllText(Path.Combine(_tempDir, "input.txt"), "input");
        var rule = new BuildRuleConfig("generate", "unused", ["input.txt"], ["out.txt"]);
        using var checker = new IncrementalChecker(_tempDir, "rules");

        Assert.AreEqual(0, await BuildRuleRunner.RunAsync(
            [rule], _tempDir, "host", "debug", _tempDir, checker, dryRun: true));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "out.txt")));
    }
}
