using Dotori.Grpc;
using Grpc.Net.Client;

namespace Dotori.BuildServer.Workers;

/// <summary>
/// Tracks registered worker agents and distributes jobs to them.
/// Workers are registered via environment variable DOTORI_WORKERS
/// (comma-separated gRPC addresses, e.g. "http://worker1:5100,http://worker2:5100").
/// </summary>
public sealed class WorkerPool : IDisposable
{
    private readonly List<WorkerEntry> _workers = [];
    private int _roundRobin;

    public WorkerPool(IConfiguration config, ILogger<WorkerPool> logger)
    {
        var addresses = config["DOTORI_WORKERS"] ?? string.Empty;
        var token = RemoteBuildAuth.ResolveToken(
            config["DOTORI_WORKER_TOKEN"], config["DOTORI_WORKER_TOKEN_FILE"]);
        foreach (var addr in addresses.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = addr.Trim();
            try
            {
                var channel = GrpcChannel.ForAddress(trimmed);
                var client  = new BuildWorker.BuildWorkerClient(channel);
                var headers = new global::Grpc.Core.Metadata();
                if (!string.IsNullOrWhiteSpace(token))
                    headers.Add(RemoteBuildAuth.HeaderName, token);
                _workers.Add(new WorkerEntry(trimmed, channel, client, headers));
                logger.LogInformation("Registered worker: {Address}", trimmed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create channel for worker {Address}", trimmed);
            }
        }

        if (_workers.Count == 0)
            logger.LogWarning("No workers registered. All builds will fail without workers.");
    }

    /// <summary>
    /// Picks the next available worker (round-robin).
    /// Returns null if no workers are registered.
    /// </summary>
    public WorkerEntry? NextWorker()
    {
        if (_workers.Count == 0) return null;
        var idx = Interlocked.Increment(ref _roundRobin) % _workers.Count;
        return _workers[idx];
    }

    public IReadOnlyList<WorkerEntry> All => _workers;

    public void Dispose()
    {
        foreach (var w in _workers)
            w.Channel.Dispose();
    }
}

public sealed record WorkerEntry(
    string                        Address,
    GrpcChannel                   Channel,
    BuildWorker.BuildWorkerClient Client,
    global::Grpc.Core.Metadata    Headers);
