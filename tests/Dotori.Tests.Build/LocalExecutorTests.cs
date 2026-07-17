using System.Diagnostics;
using Dotori.Core.Build;
using Dotori.Core.Executor;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class LocalExecutorTests
{
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
