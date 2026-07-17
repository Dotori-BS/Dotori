using System.Diagnostics;
using Dotori.Core.Build;

namespace Dotori.Core.Executor;

/// <summary>Result of a single compile or link job.</summary>
public sealed class JobResult
{
    public required bool   Success  { get; init; }
    public required string Command  { get; init; }
    public required string Stdout   { get; init; }
    public required string Stderr   { get; init; }
    public required int    ExitCode { get; init; }
}

/// <summary>
/// Executes compile and link jobs locally, with optional parallelism.
/// </summary>
public sealed class LocalExecutor : IExecutor
{
    private readonly int _maxParallelism;

    public LocalExecutor(int maxParallelism = 0)
    {
        _maxParallelism = maxParallelism <= 0
            ? Environment.ProcessorCount
            : maxParallelism;
    }

    /// <summary>
    /// Execute a batch of compile jobs in parallel.
    /// </summary>
    public async Task<IReadOnlyList<JobResult>> RunCompileJobsAsync(
        string compiler,
        IReadOnlyList<CompileJob> jobs,
        CancellationToken ct = default)
    {
        var semaphore = new SemaphoreSlim(_maxParallelism);
        var tasks     = jobs.Select(job => RunWithSemaphoreAsync(
            semaphore, () => RunProcessAsync(compiler, string.Join(" ", job.Args), ct), ct));
        return await Task.WhenAll(tasks);
    }

    /// <summary>Execute a single link job.</summary>
    public async Task<JobResult> RunLinkJobAsync(
        string linker,
        LinkJob job,
        CancellationToken ct = default)
    {
        return await RunProcessAsync(linker, string.Join(" ", job.Args), ct);
    }

    private static async Task<T> RunWithSemaphoreAsync<T>(
        SemaphoreSlim sem,
        Func<Task<T>> work,
        CancellationToken ct)
    {
        await sem.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            sem.Release();
        }
    }

    private static async Task<JobResult> RunProcessAsync(
        string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                try { await proc.WaitForExitAsync(CancellationToken.None); }
                catch (InvalidOperationException) { }
            }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new JobResult
        {
            Success  = proc.ExitCode == 0,
            Command  = $"{exe} {args}",
            Stdout   = stdout,
            Stderr   = stderr,
            ExitCode = proc.ExitCode,
        };
    }
}
