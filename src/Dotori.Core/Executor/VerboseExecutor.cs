using Dotori.Core.Build;

namespace Dotori.Core.Executor;

/// <summary>Prints every compile and link command before delegating execution.</summary>
public sealed class VerboseExecutor(
    IExecutor inner, TextWriter? writer = null) : IExecutor, IDisposable
{
    private readonly TextWriter _writer = writer ?? Console.Out;

    public Task<IReadOnlyList<JobResult>> RunCompileJobsAsync(
        string compiler, IReadOnlyList<CompileJob> jobs, CancellationToken ct = default)
    {
        foreach (var job in jobs) Print(compiler, job.Args);
        return inner.RunCompileJobsAsync(compiler, jobs, ct);
    }

    public Task<JobResult> RunLinkJobAsync(
        string linker, LinkJob job, CancellationToken ct = default)
    {
        Print(linker, job.Args);
        return inner.RunLinkJobAsync(linker, job, ct);
    }

    public void Dispose()
    {
        if (inner is IDisposable disposable) disposable.Dispose();
    }

    private void Print(string executable, IEnumerable<string> args) =>
        _writer.WriteLine($"  [command] {executable} {string.Join(" ", args)}");
}
