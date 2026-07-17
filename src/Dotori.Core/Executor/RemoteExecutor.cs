using Dotori.Core.Build;
using Dotori.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;

namespace Dotori.Core.Executor;

/// <summary>
/// Executes compile and link jobs on a remote <c>dotori-server</c> (BuildCoordinator).
/// Falls back to <see cref="LocalExecutor"/> when the server is unreachable.
/// </summary>
public sealed class RemoteExecutor : IExecutor, IDisposable
{
    private readonly GrpcChannel                        _channel;
    private readonly BuildCoordinator.BuildCoordinatorClient _client;
    private readonly LocalExecutor                      _fallback;
    private readonly Metadata                           _headers;

    public RemoteExecutor(string serverAddress, int localJobs = 0)
    {
        _channel  = GrpcChannel.ForAddress(serverAddress);
        _client   = new BuildCoordinator.BuildCoordinatorClient(_channel);
        _fallback = new LocalExecutor(localJobs);
        _headers  = new Metadata();
        var token = Environment.GetEnvironmentVariable("DOTORI_SERVER_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            _headers.Add(RemoteBuildAuth.HeaderName, token);
    }

    public async Task<IReadOnlyList<JobResult>> RunCompileJobsAsync(
        string                    compiler,
        IReadOnlyList<CompileJob> jobs,
        CancellationToken         ct = default)
    {
        // Try remote; fall back to local on any transport error.
        try
        {
            var tasks = jobs.Select(job => CompileRemoteAsync(compiler, job, ct));
            return await Task.WhenAll(tasks);
        }
        catch (RpcException ex) when (IsTransportError(ex))
        {
            Console.Error.WriteLine(
                $"[dotori] Remote server unreachable ({ex.Status.Detail}). Falling back to local.");
            return await _fallback.RunCompileJobsAsync(compiler, jobs, ct);
        }
    }

    public async Task<JobResult> RunLinkJobAsync(
        string            linker,
        LinkJob           job,
        CancellationToken ct = default)
    {
        try
        {
            return await LinkRemoteAsync(linker, job, ct);
        }
        catch (RpcException ex) when (IsTransportError(ex))
        {
            Console.Error.WriteLine(
                $"[dotori] Remote server unreachable ({ex.Status.Detail}). Falling back to local.");
            return await _fallback.RunLinkJobAsync(linker, job, ct);
        }
    }

    private async Task<JobResult> CompileRemoteAsync(
        string compiler, CompileJob job, CancellationToken ct)
    {
        var sourceBytes = await ReadSourceAsync(job.SourceFile, ct);
        var sourceHash  = ComputeHash(sourceBytes);

        var request = new CompileRequest
        {
            Compiler     = compiler,
            TargetTriple = string.Empty,  // coordinator will infer from worker
            SourceHash   = sourceHash,
            SourceBytes  = ByteString.CopyFrom(sourceBytes),
            SourcePath   = Path.GetFileName(job.SourceFile),
        };
        request.Args.AddRange(job.Args);

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        CompileResult? result = null;

        using var call = _client.Compile(request, headers: _headers, cancellationToken: ct);
        await foreach (var ev in call.ResponseStream.ReadAllAsync(ct))
        {
            if (ev.PayloadCase == CompileEvent.PayloadOneofCase.Log)
            {
                if (ev.Log.Stream == LogLine.Types.Stream.Stdout)
                    stdout.AppendLine(ev.Log.Text);
                else
                    stderr.AppendLine(ev.Log.Text);
            }
            else if (ev.PayloadCase == CompileEvent.PayloadOneofCase.Result)
            {
                result = ev.Result;
            }
        }

        if (result is null)
            return Failure(job, "No result received from server.");

        // Write received .obj file to expected output location
        if (result.Success && result.ObjBytes.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.OutputFile)!);
            await File.WriteAllBytesAsync(job.OutputFile, result.ObjBytes.ToByteArray(), ct);
        }

        return new JobResult
        {
            Success  = result.Success,
            Command  = $"[remote] {compiler} {string.Join(" ", job.Args)}",
            Stdout   = stdout.ToString(),
            Stderr   = stderr.ToString(),
            ExitCode = result.ExitCode,
        };
    }

    private async Task<JobResult> LinkRemoteAsync(
        string linker, LinkJob job, CancellationToken ct)
    {
        var request = new LinkRequest
        {
            Linker     = linker,
            OutputName = Path.GetFileName(job.OutputFile),
        };
        request.Args.AddRange(job.Args);

        foreach (var obj in job.InputFiles)
        {
            var bytes = File.Exists(obj) ? await File.ReadAllBytesAsync(obj, ct) : [];
            request.ObjFiles.Add(ByteString.CopyFrom(bytes));
        }

        var response = await _client.LinkAsync(request, headers: _headers, cancellationToken: ct);

        if (response.Success && response.OutputBytes.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.OutputFile)!);
            await File.WriteAllBytesAsync(job.OutputFile, response.OutputBytes.ToByteArray(), ct);
        }

        return new JobResult
        {
            Success  = response.Success,
            Command  = $"[remote] {linker} {string.Join(" ", job.Args)}",
            Stdout   = response.Stdout,
            Stderr   = response.Stderr,
            ExitCode = response.ExitCode,
        };
    }

    private static async Task<byte[]> ReadSourceAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        return await File.ReadAllBytesAsync(path, ct);
    }

    private static string ComputeHash(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static JobResult Failure(CompileJob job, string message) =>
        new()
        {
            Success  = false,
            Command  = string.Join(" ", job.Args),
            Stdout   = string.Empty,
            Stderr   = message,
            ExitCode = -1,
        };

    private static bool IsTransportError(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded;

    public void Dispose() => _channel.Dispose();
}
