using Dotori.Grpc;
using Grpc.Core;
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
    private readonly TimeSpan _failureCooldown;
    private readonly TimeSpan _healthTimeout;
    private int _roundRobin;

    public WorkerPool(IConfiguration config, ILogger<WorkerPool> logger)
    {
        _failureCooldown = TimeSpan.FromSeconds(Math.Max(1,
            config.GetValue("DOTORI_WORKER_FAILURE_COOLDOWN_SECONDS", 30)));
        _healthTimeout = TimeSpan.FromSeconds(Math.Max(1,
            config.GetValue("DOTORI_WORKER_HEALTH_TIMEOUT_SECONDS", 5)));
        var addresses = config["DOTORI_WORKERS"] ?? string.Empty;
        var token = RemoteBuildAuth.ResolveToken(
            config["DOTORI_WORKER_TOKEN"], config["DOTORI_WORKER_TOKEN_FILE"]);
        foreach (var addr in addresses.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = addr.Trim();
            try
            {
                var channel = GrpcChannel.ForAddress(trimmed, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 });
                var client  = new BuildWorker.BuildWorkerClient(channel);
                var headers = new global::Grpc.Core.Metadata();
                if (!string.IsNullOrWhiteSpace(token))
                    headers.Add(RemoteBuildAuth.HeaderName, token);
                _workers.Add(new WorkerEntry(trimmed, channel, client, headers, _failureCooldown));
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
        return Candidates().FirstOrDefault();
    }

    /// <summary>Returns workers in round-robin order, preferring entries outside failure cooldown.</summary>
    public IReadOnlyList<WorkerEntry> Candidates()
    {
        if (_workers.Count == 0) return [];
        var start = Math.Abs(Interlocked.Increment(ref _roundRobin) % _workers.Count);
        var ordered = Enumerable.Range(0, _workers.Count)
            .Select(offset => _workers[(start + offset) % _workers.Count])
            .ToArray();
        var healthy = ordered.Where(worker => worker.IsAvailable).ToArray();
        return healthy.Length > 0 ? healthy : ordered;
    }

    /// <summary>Probes unknown workers and returns only those supporting the requested triple.</summary>
    public async Task<IReadOnlyList<WorkerEntry>> CandidatesAsync(
        string targetTriple, CancellationToken ct, string? hostOs = null, string? hostArch = null)
    {
        var candidates = Candidates();
        await RefreshCapabilitiesAsync(candidates, ct);
        return candidates.Where(worker =>
                worker.IsAvailable && (hostOs is null ? worker.SupportsTarget(targetTriple) :
                    worker.SupportsBundleHost(hostOs, hostArch!)))
            .ToArray();
    }

    public Task RefreshCapabilitiesAsync(CancellationToken ct) =>
        RefreshCapabilitiesAsync(_workers, ct);

    private async Task RefreshCapabilitiesAsync(
        IEnumerable<WorkerEntry> workers, CancellationToken ct)
    {
        foreach (var worker in workers.Where(worker => !worker.CapabilitiesKnown))
        {
            try
            {
                var health = await worker.Client.HealthAsync(
                    new HealthRequest(), worker.Headers,
                    deadline: DateTime.UtcNow.Add(_healthTimeout), cancellationToken: ct);
                if (!health.Ok) throw new RpcException(new Status(StatusCode.Unavailable, "Worker unhealthy"));
                worker.SetBundleHost(health.HostOs, health.HostArch, health.UploadedToolchains);
                worker.SetSupportedTargets(health.SupportedTargets);
                worker.ReportSuccess();
            }
            catch (RpcException) when (!ct.IsCancellationRequested)
            {
                worker.ReportTransportFailure();
            }
        }
    }

    public IReadOnlyList<WorkerEntry> All => _workers;

    public void Dispose()
    {
        foreach (var w in _workers)
            w.Channel.Dispose();
    }
}

public sealed class WorkerEntry(
    string address,
    GrpcChannel channel,
    BuildWorker.BuildWorkerClient client,
    global::Grpc.Core.Metadata headers,
    TimeSpan failureCooldown)
{
    private int _consecutiveFailures;
    private long _retryAfterTicks;
    private string[]? _supportedTargets;
    private string _hostOs = "";
    private string _hostArch = "";
    private bool _uploadedToolchains;

    public string Address { get; } = address;
    public GrpcChannel Channel { get; } = channel;
    public BuildWorker.BuildWorkerClient Client { get; } = client;
    public global::Grpc.Core.Metadata Headers { get; } = headers;
    public bool IsAvailable => DateTime.UtcNow.Ticks >= Interlocked.Read(ref _retryAfterTicks);
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);
    public bool CapabilitiesKnown => Volatile.Read(ref _supportedTargets) is not null;
    public IReadOnlyList<string> SupportedTargets =>
        Volatile.Read(ref _supportedTargets) ?? [];

    public void SetSupportedTargets(IEnumerable<string> targets) =>
        Volatile.Write(ref _supportedTargets,
            targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    public bool SupportsTarget(string targetTriple) =>
        Volatile.Read(ref _supportedTargets)?.Contains(
            targetTriple, StringComparer.OrdinalIgnoreCase) == true;

    public void SetBundleHost(string os, string arch, bool enabled)
    { _hostOs = os; _hostArch = arch; _uploadedToolchains = enabled; }

    public bool SupportsBundleHost(string os, string arch) =>
        _uploadedToolchains && _hostOs == os && _hostArch == arch;

    public void ReportSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _retryAfterTicks, 0);
    }

    public void ReportTransportFailure()
    {
        var failures = Math.Min(Interlocked.Increment(ref _consecutiveFailures), 5);
        var delayTicks = failureCooldown.Ticks * (1L << (failures - 1));
        Interlocked.Exchange(ref _retryAfterTicks, DateTime.UtcNow.AddTicks(delayTicks).Ticks);
    }
}
