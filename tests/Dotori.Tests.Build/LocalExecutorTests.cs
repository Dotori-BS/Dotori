using System.Diagnostics;
using Dotori.Core.Build;
using Dotori.Core.Executor;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class LocalExecutorTests
{
    [TestMethod]
    public void Core_DoesNotReferenceRemoteTransportAssemblies()
    {
        var references = typeof(IExecutor).Assembly.GetReferencedAssemblies();

        Assert.IsFalse(references.Any(reference =>
            reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true ||
            reference.Name == "Google.Protobuf"));
    }

    [TestMethod]
    public async Task VerboseExecutor_PrintsCompileAndLinkCommands()
    {
        var writer = new StringWriter();
        using var executor = new VerboseExecutor(new DryRunExecutor(), writer);
        var compile = new CompileJob
        {
            SourceFile = "main.cpp", OutputFile = "main.o", Args = ["-c", "main.cpp"],
        };
        var link = new LinkJob
        {
            InputFiles = ["main.o"], OutputFile = "app", Args = ["main.o", "-o", "app"],
        };

        await executor.RunCompileJobsAsync("clang++", [compile]);
        await executor.RunLinkJobAsync("clang++", link);

        var output = writer.ToString();
        StringAssert.Contains(output, "[command] clang++ -c main.cpp");
        StringAssert.Contains(output, "[command] clang++ main.o -o app");
    }

    [TestMethod]
    public async Task DryRunExecutor_ReportsCommandWithoutCreatingOutput()
    {
        var output = Path.Combine(Path.GetTempPath(), $"dotori-dry-{Guid.NewGuid():N}.o");
        var job = new CompileJob
        {
            SourceFile = "main.cpp",
            OutputFile = output,
            Args = ["-c", "main.cpp", "-o", output],
        };

        var results = await new DryRunExecutor().RunCompileJobsAsync("clang++", [job]);

        Assert.IsTrue(results[0].Success);
        StringAssert.Contains(results[0].Stdout, "[dry-run] clang++ -c main.cpp");
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public void ResponseFileArguments_ShortCommand_RemainsInline()
    {
        using var invocation = ResponseFileArguments.Create(["-c", "main.cpp"], threshold: 100);

        CollectionAssert.AreEqual(new[] { "-c", "main.cpp" }, invocation.Arguments.ToArray());
    }

    [TestMethod]
    public void ResponseFileArguments_LongCommand_WritesEscapedResponseFile()
    {
        using var invocation = ResponseFileArguments.Create(
            ["-DNAME=\"hello world\"", "\"C:\\source dir\\main.cpp\""], threshold: 1);

        Assert.HasCount(1, invocation.Arguments);
        Assert.StartsWith("@", invocation.Arguments[0]);
        var path = invocation.Arguments[0][1..];
        Assert.IsTrue(File.Exists(path));
        var contents = File.ReadAllLines(path);
        CollectionAssert.AreEqual(new[]
        {
            "\"-DNAME=hello world\"",
            "\"C:\\source dir\\main.cpp\"",
        }, contents);
    }

    [TestMethod]
    public void ProcessArguments_ExpandsFlagsAndPreservesQuotedPaths()
    {
        var args = ProcessArguments.Expand([
            "-Wall -Wextra",
            "-I\"/tmp/project with spaces/include\"",
            "-DNAME=\"hello world\"",
        ]);

        CollectionAssert.AreEqual(new[]
        {
            "-Wall", "-Wextra", "/tmp/project with spaces/include".Insert(0, "-I"),
            "-DNAME=hello world",
        }, args.ToArray());
    }

    [TestMethod]
    public async Task RunCompileJobsAsync_Cancellation_StopsProcessPromptly()
    {
        if (OperatingSystem.IsWindows()) return;

        var job = new CompileJob
        {
            SourceFile = "unused",
            OutputFile = "unused.o",
            Args = ["-c", "\"sleep 30\""],
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new LocalExecutor(1).RunCompileJobsAsync("/bin/sh", [job], cts.Token));

        Assert.IsLessThan(TimeSpan.FromSeconds(5), stopwatch.Elapsed);
    }
}
