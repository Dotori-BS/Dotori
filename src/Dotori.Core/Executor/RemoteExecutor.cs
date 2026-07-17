using Dotori.Core.Build;
using Dotori.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Dotori.Core.Executor;

/// <summary>
/// Executes compile and link jobs on a remote <c>dotori-server</c> (BuildCoordinator).
/// Falls back to <see cref="LocalExecutor"/> when the server is unreachable.
/// </summary>
public sealed class RemoteExecutor : IExecutor, IDisposable
{
    private static readonly ConcurrentDictionary<string, string> CompilerFingerprints = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly GrpcChannel                        _channel;
    private readonly BuildCoordinator.BuildCoordinatorClient _client;
    private readonly LocalExecutor                      _fallback;
    private readonly Metadata                           _headers;
    private readonly string                             _targetTriple;
    internal string TargetTriple => _targetTriple;

    public RemoteExecutor(string serverAddress, string targetTriple, int localJobs = 0)
    {
        _channel  = GrpcChannel.ForAddress(serverAddress);
        _client   = new BuildCoordinator.BuildCoordinatorClient(_channel);
        _fallback = new LocalExecutor(localJobs);
        _headers  = new Metadata();
        _targetTriple = targetTriple;
        var token = RemoteBuildAuth.ResolveToken(
            Environment.GetEnvironmentVariable("DOTORI_SERVER_TOKEN"),
            Environment.GetEnvironmentVariable("DOTORI_SERVER_TOKEN_FILE"));
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
        var compilerFingerprint = CompilerFingerprints.GetOrAdd(compiler, ComputeCompilerFingerprint);
        var dependencyHash = ComputeDependencyHash(job.SourceFile, job.Args);

        var request = new CompileRequest
        {
            Compiler     = compiler,
            TargetTriple = _targetTriple,
            SourceHash   = sourceHash,
            SourceBytes  = ByteString.CopyFrom(sourceBytes),
            SourcePath   = Path.GetFileName(job.SourceFile),
            CompilerFingerprint = compilerFingerprint,
            DependencyHash = dependencyHash,
        };
        request.Args.AddRange(RemoteArgumentMapper.MapCompile(job));

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
            TargetTriple = _targetTriple,
            OutputName = Path.GetFileName(job.OutputFile),
        };
        request.Args.AddRange(RemoteArgumentMapper.MapLink(job));

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

    private static string ComputeCompilerFingerprint(string compiler)
    {
        var path = ResolveExecutable(compiler);
        if (path is null) return ComputeHash(Encoding.UTF8.GetBytes(compiler));
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string? ResolveExecutable(string executable)
    {
        if (File.Exists(executable)) return Path.GetFullPath(executable);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string ComputeDependencyHash(string sourceFile, IReadOnlyList<string> args)
    {
        var searchPaths = args
            .Where(arg => arg.StartsWith("-I", StringComparison.Ordinal) ||
                          arg.StartsWith("/I", StringComparison.OrdinalIgnoreCase))
            .Select(arg => arg[2..].Trim('"'))
            .Where(path => path.Length > 0)
            .Select(path => Path.GetFullPath(path))
            .ToList();
        var tree = IncludeScanner.BuildTree(sourceFile, searchPaths, includeSystem: false);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void Collect(IncludeScanner.IncludeNode node, HashSet<string> output)
        {
            if (node.IsResolved && File.Exists(node.FilePath)) output.Add(node.FilePath);
            foreach (var child in node.Children) Collect(child, output);
        }
        Collect(tree, files);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFullPath(file)));
            using var stream = File.OpenRead(file);
            var contentHash = SHA256.HashData(stream);
            hash.AppendData(contentHash);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
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
