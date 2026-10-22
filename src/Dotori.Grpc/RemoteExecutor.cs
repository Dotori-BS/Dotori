using Dotori.Core.Build;
using Dotori.Core.Executor;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Dotori.Grpc;

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
    private readonly RemoteBundleClient? _bundleClient;
    internal string TargetTriple => _targetTriple;

    public RemoteExecutor(string serverAddress, string targetTriple, int localJobs = 0, string? toolchainConfiguration = null)
    {
        _bundleClient = toolchainConfiguration is null ? null : new RemoteBundleClient(toolchainConfiguration);
        _channel  = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 });
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
        if (_bundleClient is not null && jobs.Any(RequiresLocalCompilerState))
        {
            Console.Error.WriteLine("[dotori] PCH/module compiler state contains machine-local paths; compiling this batch locally.");
            return await _fallback.RunCompileJobsAsync(compiler, jobs, ct);
        }
        // Swift whole-module jobs contain multiple source files, while the remote
        // protocol currently transports one primary source. Keep them local until
        // the protocol can describe multi-input compile actions losslessly.
        if (Path.GetFileNameWithoutExtension(compiler)
            .Equals("swiftc", StringComparison.OrdinalIgnoreCase))
        {
            if (_bundleClient is not null) Console.Error.WriteLine("[dotori] Swift compilation remains local.");
            return await _fallback.RunCompileJobsAsync(compiler, jobs, ct);
        }

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
        catch (Exception ex) when (_bundleClient is not null && ex is IOException or ArgumentException or RpcException or UnauthorizedAccessException)
        {
            return jobs.Select(job => Failure(job, $"Bundled remote compile failed: {ex.Message}")).ToArray();
        }
    }

    public async Task<JobResult> RunLinkJobAsync(
        string            linker,
        LinkJob           job,
        CancellationToken ct = default)
    {
        if (_bundleClient is not null && job.InputFiles.Any(path =>
            Path.GetExtension(path).ToLowerInvariant() is ".pcm" or ".ifc"))
        {
            Console.Error.WriteLine("[dotori] Linking compiler module state remains local.");
            return await _fallback.RunLinkJobAsync(linker, job, ct);
        }
        if (Path.GetFileNameWithoutExtension(linker)
            .Equals("swiftc", StringComparison.OrdinalIgnoreCase))
        {
            if (_bundleClient is not null) Console.Error.WriteLine("[dotori] Swift linking remains local.");
            return await _fallback.RunLinkJobAsync(linker, job, ct);
        }

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
        catch (Exception ex) when (_bundleClient is not null && ex is IOException or ArgumentException or RpcException or UnauthorizedAccessException)
        {
            return new JobResult { Success = false, ExitCode = -1, Command = linker,
                Stdout = "", Stderr = $"Bundled remote link failed: {ex.Message}" };
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
            Compiler     = NormalizeRemoteExecutable(compiler),
            TargetTriple = _targetTriple,
            SourceHash   = sourceHash,
            SourceBytes  = ByteString.CopyFrom(sourceBytes),
            SourcePath   = Path.GetFileName(job.SourceFile),
            CompilerFingerprint = compilerFingerprint,
            DependencyHash = dependencyHash,
        };
        request.Args.AddRange(RemoteArgumentMapper.MapCompile(job));
        if (_bundleClient is not null)
        {
            var prepared = await _bundleClient.PrepareAsync(compiler, job.Args, job.OutputFile,
                job.DependencyFiles.Prepend(job.SourceFile), ct);
            request.Bundle = prepared.Bundle;
            request.Args.Clear(); request.Args.AddRange(prepared.Arguments);
            request.SourceBytes = ByteString.Empty;
            await _bundleClient.UploadAsync(prepared.Bundle, _client, _headers, ct);
        }

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

        if (result.Success && _bundleClient is not null)
            await _bundleClient.SaveOutputsAsync(result.Outputs, job.OutputFile, _client, _headers, ct);

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
            Linker     = NormalizeRemoteExecutable(linker),
            TargetTriple = _targetTriple,
            OutputName = Path.GetFileName(job.OutputFile),
        };
        request.Args.AddRange(RemoteArgumentMapper.MapLink(job));

        if (_bundleClient is not null)
        {
            var prepared = await _bundleClient.PrepareAsync(linker, job.Args, job.OutputFile, job.InputFiles, ct);
            request.Bundle = prepared.Bundle;
            request.Args.Clear(); request.Args.AddRange(prepared.Arguments);
            await _bundleClient.UploadAsync(prepared.Bundle, _client, _headers, ct);
        }
        else foreach (var obj in job.InputFiles)
        {
            var bytes = File.Exists(obj) ? await File.ReadAllBytesAsync(obj, ct) : [];
            request.ObjFiles.Add(ByteString.CopyFrom(bytes));
        }

        var response = await _client.LinkAsync(request, headers: _headers, cancellationToken: ct);

        if (response.Success && _bundleClient is not null)
            await _bundleClient.SaveOutputsAsync(response.Outputs, job.OutputFile, _client, _headers, ct);

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

    internal static string NormalizeRemoteExecutable(string executable)
    {
        var fileName = Path.GetFileName(executable);
        return string.IsNullOrWhiteSpace(fileName) ? executable : fileName;
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

    private static bool RequiresLocalCompilerState(CompileJob job) =>
        Path.GetExtension(job.SourceFile).ToLowerInvariant() is ".cppm" or ".ixx" ||
        job.Args.Any(argument => argument.Contains("-include-pch", StringComparison.Ordinal) ||
            argument.Contains("c++-header", StringComparison.Ordinal) ||
            argument.Contains("-fmodule", StringComparison.Ordinal) ||
            argument.Contains("-fprebuilt-module-path", StringComparison.Ordinal) ||
            argument.Contains("--precompile", StringComparison.Ordinal) ||
            argument.StartsWith("/Yc", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("/Yu", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("/reference", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("/headerUnit", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("/interface", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("/ifc", StringComparison.OrdinalIgnoreCase));

    internal static bool IsTransportError(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded ||
        ex.StatusCode == StatusCode.Internal &&
        (ex.Status.Detail.Contains("Error starting gRPC call", StringComparison.OrdinalIgnoreCase) ||
         ex.Status.Detail.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase) ||
         ex.Status.Detail.Contains("HttpProtocolException", StringComparison.OrdinalIgnoreCase));

    public void Dispose() => _channel.Dispose();
}
