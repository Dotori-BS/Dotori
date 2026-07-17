using System.Diagnostics;
using Dotori.Core.Build;

namespace Dotori.Tests.Build;

/// <summary>
/// Tests for ScriptRunner.RunAsync — verifies shell execution, env vars,
/// working directory, and error propagation.
/// </summary>
[TestClass]
public sealed class RunScriptsTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        // Resolve symlinks (macOS: /var is a symlink to /private/var)
        // Use a subprocess instead of Environment.CurrentDirectory to avoid a race
        // condition when tests run in parallel (CWD is process-global state).
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo("realpath", _tempDir)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                })!;
                proc.WaitForExit();
                var resolved = proc.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(resolved))
                    _tempDir = resolved;
            }
            catch { /* fall through to original path */ }
        }
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Empty list ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunScripts_EmptyList_ReturnsZero()
    {
        var code = await ScriptRunner.RunAsync(
            [], _tempDir, "linux-x64", "debug", _tempDir);
        Assert.AreEqual(0, code);
    }

    // ── Successful commands ─────────────────────────────────────────────────

    [TestMethod]
    public async Task RunScripts_EchoCommand_ReturnsZero()
    {
        var cmd = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";
        var code = await ScriptRunner.RunAsync(
            [cmd], _tempDir, "linux-x64", "debug", _tempDir);
        Assert.AreEqual(0, code);
    }

    [TestMethod]
    public async Task RunScripts_CreatesFile_FileExists()
    {
        var outFile = Path.Combine(_tempDir, "created.txt");
        string cmd;
        if (OperatingSystem.IsWindows())
            cmd = $"echo created > \"{outFile}\"";
        else
            cmd = $"touch \"{outFile}\"";

        var code = await ScriptRunner.RunAsync(
            [cmd], _tempDir, "linux-x64", "debug", _tempDir);

        Assert.AreEqual(0, code);
        Assert.IsTrue(File.Exists(outFile));
    }

    // ── Failing command ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunScripts_FailingCommand_ReturnsNonZero()
    {
        // "exit 42" on Unix / "exit /b 42" on Windows returns exit code 42
        var cmd = OperatingSystem.IsWindows() ? "exit /b 42" : "exit 42";
        var code = await ScriptRunner.RunAsync(
            [cmd], _tempDir, "linux-x64", "debug", _tempDir);
        Assert.AreNotEqual(0, code);
    }

    [TestMethod]
    public async Task RunScripts_Cancellation_StopsProcessTreePromptly()
    {
        var command = OperatingSystem.IsWindows()
            ? "ping -n 31 127.0.0.1 > nul"
            : "sleep 30";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ScriptRunner.RunAsync(
                [command], _tempDir, "host", "debug", _tempDir, cts.Token));

        Assert.IsLessThan(TimeSpan.FromSeconds(5), stopwatch.Elapsed);
    }

    [TestMethod]
    public async Task RunScripts_StopsOnFirstFailure()
    {
        var outFile = Path.Combine(_tempDir, "should_not_exist.txt");
        var failCmd  = OperatingSystem.IsWindows() ? "exit /b 1" : "exit 1";
        string createCmd;
        if (OperatingSystem.IsWindows())
            createCmd = $"echo x > \"{outFile}\"";
        else
            createCmd = $"touch \"{outFile}\"";

        var code = await ScriptRunner.RunAsync(
            [failCmd, createCmd], _tempDir, "linux-x64", "debug", _tempDir);

        Assert.AreNotEqual(0, code);
        Assert.IsFalse(File.Exists(outFile), "Second command should not have run");
    }

    // ── Environment variables ───────────────────────────────────────────────

    [TestMethod]
    public async Task RunScripts_EnvVars_TargetIsPassed()
    {
        var outFile = Path.Combine(_tempDir, "target.txt");
        string cmd;
        if (OperatingSystem.IsWindows())
            cmd = $"echo %DOTORI_TARGET% > \"{outFile}\"";
        else
            cmd = $"echo $DOTORI_TARGET > \"{outFile}\"";

        await ScriptRunner.RunAsync(
            [cmd], _tempDir, "macos-arm64", "debug", _tempDir);

        Assert.IsTrue(File.Exists(outFile));
        var content = File.ReadAllText(outFile).Trim();
        Assert.Contains("macos-arm64", content,
            $"Expected 'macos-arm64' in output but got: '{content}'");
    }

    [TestMethod]
    public async Task RunScripts_EnvVars_ConfigIsPassed()
    {
        var outFile = Path.Combine(_tempDir, "config.txt");
        string cmd;
        if (OperatingSystem.IsWindows())
            cmd = $"echo %DOTORI_CONFIG% > \"{outFile}\"";
        else
            cmd = $"echo $DOTORI_CONFIG > \"{outFile}\"";

        await ScriptRunner.RunAsync(
            [cmd], _tempDir, "linux-x64", "release", _tempDir);

        var content = File.ReadAllText(outFile).Trim();
        Assert.Contains("release", content,
            $"Expected 'release' in output but got: '{content}'");
    }

    // ── Working directory ───────────────────────────────────────────────────

    [TestMethod]
    public async Task RunScripts_WorkingDir_IsProjectDir()
    {
        var outFile = Path.Combine(_tempDir, "cwd.txt");
        string cmd;
        if (OperatingSystem.IsWindows())
            cmd = $"cd > \"{outFile}\"";
        else
            cmd = $"pwd > \"{outFile}\"";

        await ScriptRunner.RunAsync(
            [cmd], _tempDir, "linux-x64", "debug", _tempDir);

        var content = File.ReadAllText(outFile).Trim();
        // The working directory path should match _tempDir (case-insensitive on macOS/Windows)
        Assert.IsTrue(content.Equals(_tempDir, StringComparison.OrdinalIgnoreCase)
                   || content.StartsWith(_tempDir, StringComparison.OrdinalIgnoreCase),
            $"Expected CWD '{_tempDir}' but got '{content}'");
    }

    // ── Multiple commands run in order ──────────────────────────────────────

    [TestMethod]
    public async Task RunScripts_MultipleCommands_RunInOrder()
    {
        var outFile = Path.Combine(_tempDir, "order.txt");
        string cmd1, cmd2;
        if (OperatingSystem.IsWindows())
        {
            cmd1 = $"echo first >> \"{outFile}\"";
            cmd2 = $"echo second >> \"{outFile}\"";
        }
        else
        {
            cmd1 = $"echo first >> \"{outFile}\"";
            cmd2 = $"echo second >> \"{outFile}\"";
        }

        var code = await ScriptRunner.RunAsync(
            [cmd1, cmd2], _tempDir, "linux-x64", "debug", _tempDir);

        Assert.AreEqual(0, code);
        var lines = File.ReadAllLines(outFile);
        Assert.IsGreaterThanOrEqualTo(lines.Length, 2);
        Assert.AreEqual("first", lines[0].Trim());
        Assert.AreEqual("second", lines[1].Trim());
    }
}
