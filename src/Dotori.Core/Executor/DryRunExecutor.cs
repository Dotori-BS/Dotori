using Dotori.Core.Build;

namespace Dotori.Core.Executor;

/// <summary>Reports build commands without starting external processes.</summary>
public sealed class DryRunExecutor : IExecutor
{
    public Task<IReadOnlyList<JobResult>> RunCompileJobsAsync(
        string compiler, IReadOnlyList<CompileJob> jobs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<JobResult>>(
            jobs.Select(job => Success(compiler, job.Args)).ToArray());
    }

    public Task<JobResult> RunLinkJobAsync(
        string linker, LinkJob job, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Success(linker, job.Args));
    }

    private static JobResult Success(string executable, IReadOnlyList<string> args)
    {
        var command = $"{executable} {string.Join(" ", args)}";
        return new JobResult
        {
            Success = true,
            Command = command,
            Stdout = $"  [dry-run] {command}{Environment.NewLine}",
            Stderr = string.Empty,
            ExitCode = 0,
        };
    }
}
