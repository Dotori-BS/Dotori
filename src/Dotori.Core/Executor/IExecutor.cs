using Dotori.Core.Build;

namespace Dotori.Core.Executor;

/// <summary>
/// Abstraction over local and remote build execution.
/// Local implementations live in Core; the remote implementation lives in Dotori.Grpc.
/// </summary>
public interface IExecutor
{
    Task<IReadOnlyList<JobResult>> RunCompileJobsAsync(
        string                    compiler,
        IReadOnlyList<CompileJob> jobs,
        CancellationToken         ct = default);

    Task<JobResult> RunLinkJobAsync(
        string            linker,
        LinkJob           job,
        CancellationToken ct = default);
}
