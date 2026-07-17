using System.Diagnostics;
using Dotori.Core.Build;
using Dotori.Core.Executor;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class LocalExecutorTests
{
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
    public void RemoteArgumentMapper_ReplacesCompilePathsWithoutDuplicates()
    {
        var job = new CompileJob
        {
            SourceFile = "/local/src/main.cpp",
            OutputFile = "/local/obj/main.o",
            Args = ["-c", "\"/local/src/main.cpp\"", "-o \"/local/obj/main.o\""],
        };

        var mapped = RemoteArgumentMapper.MapCompile(job);
        var materialized = RemoteArgumentMapper.MaterializeCompile(
            mapped, "/worker/main.cpp", "/worker/main.o");

        Assert.IsFalse(materialized.Any(arg => arg.Contains("/local/")));
        Assert.AreEqual(1, materialized.Count(arg => arg.Contains("/worker/main.cpp")));
        Assert.AreEqual(1, materialized.Count(arg => arg.Contains("/worker/main.o")));
    }

    [TestMethod]
    public void RemoteArgumentMapper_PreservesArchiverArgumentShape()
    {
        var job = new LinkJob
        {
            InputFiles = ["/local/a.o", "/local/b.o"],
            OutputFile = "/local/libsample.a",
            Args = ["rcs", "\"/local/libsample.a\"", "\"/local/a.o\"", "\"/local/b.o\""],
        };

        var mapped = RemoteArgumentMapper.MapLink(job);
        var materialized = RemoteArgumentMapper.MaterializeLink(
            mapped, ["/worker/input_0.o", "/worker/input_1.o"], "/worker/libsample.a");

        CollectionAssert.AreEqual(new[]
        {
            "rcs", "\"/worker/libsample.a\"", "\"/worker/input_0.o\"", "\"/worker/input_1.o\"",
        }, materialized.ToArray());
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
