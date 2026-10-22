using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Dotori.Core.Toolchain;
using Dotori.Core.Executor;
using Dotori.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Dotori.Worker.Services;

/// <summary>
/// gRPC implementation of <see cref="BuildWorker"/>.
/// Receives compile/link requests, executes the compiler locally,
/// and streams results back to the coordinator.
/// </summary>
public sealed partial class WorkerService(
    ILogger<WorkerService> logger,
    IConfiguration? configuration = null,
    RemoteContentStore? contentStore = null)
    : BuildWorker.BuildWorkerBase
{
    private static readonly string[] DefaultExecutables =
    [
        "clang", "clang++", "clang-cl", "gcc", "g++", "cc", "c++", "cl",
        "link", "lld", "ld", "ld.lld", "wasm-ld", "ar", "llvm-ar", "lib",
        "emcc", "em++", "nasm", "yasm", "as", "ml", "ml64", "rc", "llvm-rc",
    ];

    private readonly HashSet<string> _allowedExecutables = ParseAllowedExecutables(
        configuration?["DOTORI_ALLOWED_EXECUTABLES"]);
    private readonly long _maxInputBytes = configuration?.GetValue<long?>("DOTORI_MAX_INPUT_BYTES")
        ?? 64L * 1024 * 1024;
    private readonly int _maxArguments = configuration?.GetValue<int?>("DOTORI_MAX_ARGUMENTS") ?? 4096;
    private readonly TimeSpan _executionTimeout = TimeSpan.FromSeconds(
        configuration?.GetValue<int?>("DOTORI_EXECUTION_TIMEOUT_SECONDS") ?? 300);
    private readonly Lazy<IReadOnlyList<string>> _supportedTargets = new(
        () => ResolveSupportedTargets(configuration?["DOTORI_SUPPORTED_TARGETS"]),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public override async Task Compile(
        WorkerCompileRequest                          request,
        IServerStreamWriter<WorkerCompileEvent>       responseStream,
        ServerCallContext                             context)
    {
        if (request.Bundle is not null)
        {
            var result = await RunBundleAsync(request.Bundle, request.Args, context.CancellationToken);
            foreach (var (stream, text) in new[] { (LogLine.Types.Stream.Stdout, result.Stdout), (LogLine.Types.Stream.Stderr, result.Stderr) })
                if (text.Length > 0)
                    await responseStream.WriteAsync(new WorkerCompileEvent { Log = new LogLine { Stream = stream, Text = text } }, context.CancellationToken);
            var response = new WorkerCompileResult { Success = result.Success, ExitCode = result.ExitCode };
            response.Outputs.AddRange(result.Outputs);
            await responseStream.WriteAsync(new WorkerCompileEvent { Result = response }, context.CancellationToken);
            return;
        }
        if (!IsAllowedExecutable(request.Compiler))
        {
            await responseStream.WriteAsync(new WorkerCompileEvent
            {
                Result = new WorkerCompileResult { Success = false, ExitCode = -1 },
            }, context.CancellationToken);
            return;
        }
        if (request.SourceBytes.Length > _maxInputBytes || !AreArgumentsValid(request.Args))
        {
            await responseStream.WriteAsync(new WorkerCompileEvent
            {
                Result = new WorkerCompileResult { Success = false, ExitCode = -1 },
            }, context.CancellationToken);
            return;
        }

        // Write source file to a temp directory
        var tmpDir     = Directory.CreateTempSubdirectory("dotori-worker-").FullName;
        var sourceFile = Path.Combine(tmpDir, Path.GetFileName(request.SourcePath));
        var objFile    = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(request.SourcePath) + ".o");

        try
        {
            await File.WriteAllBytesAsync(sourceFile, request.SourceBytes.ToByteArray(),
                context.CancellationToken);

            var args = RemoteArgumentMapper.MaterializeCompile(
                request.Args, sourceFile, objFile);

            logger.LogDebug("Compile: {Compiler} {Args}", request.Compiler, string.Join(" ", args));

            var psi = new ProcessStartInfo(request.Compiler)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var responseFile = ResponseFileArguments.Create(args, directory: tmpDir);
            foreach (var argument in responseFile.Arguments)
                psi.ArgumentList.Add(argument);

            using var proc = new Process { StartInfo = psi };
            try
            {
                proc.Start();
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                logger.LogWarning(ex, "Unable to start compiler {Compiler}", request.Compiler);
                await responseStream.WriteAsync(new WorkerCompileEvent
                {
                    Log = new LogLine
                    {
                        Stream = LogLine.Types.Stream.Stderr,
                        Text = $"Unable to start compiler '{request.Compiler}': {ex.Message}",
                    },
                }, context.CancellationToken);
                await responseStream.WriteAsync(new WorkerCompileEvent
                {
                    Result = new WorkerCompileResult { Success = false, ExitCode = 127 },
                }, context.CancellationToken);
                return;
            }

            // Stream stderr line by line
            var stderrTask = StreamStderrAsync(proc, responseStream, context.CancellationToken);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(context.CancellationToken);

            var exited = await WaitForExitAsync(proc, context.CancellationToken);
            var stdout = await stdoutTask;
            await stderrTask;

            if (!exited)
            {
                await responseStream.WriteAsync(new WorkerCompileEvent
                {
                    Result = new WorkerCompileResult { Success = false, ExitCode = -1 },
                }, context.CancellationToken);
                return;
            }

            if (!string.IsNullOrEmpty(stdout))
            {
                await responseStream.WriteAsync(new WorkerCompileEvent
                {
                    Log = new LogLine { Stream = LogLine.Types.Stream.Stdout, Text = stdout }
                }, context.CancellationToken);
            }

            byte[] objBytes = proc.ExitCode == 0 && File.Exists(objFile)
                ? await File.ReadAllBytesAsync(objFile, context.CancellationToken)
                : [];

            await responseStream.WriteAsync(new WorkerCompileEvent
            {
                Result = new WorkerCompileResult
                {
                    Success  = proc.ExitCode == 0,
                    ExitCode = proc.ExitCode,
                    ObjBytes = ByteString.CopyFrom(objBytes),
                }
            }, context.CancellationToken);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ }
        }
    }

    public override async Task<WorkerLinkResponse> Link(
        WorkerLinkRequest request,
        ServerCallContext  context)
    {
        if (request.Bundle is not null)
        {
            var result = await RunBundleAsync(request.Bundle, request.Args, context.CancellationToken);
            var response = new WorkerLinkResponse { Success = result.Success, ExitCode = result.ExitCode,
                Stdout = result.Stdout, Stderr = result.Stderr };
            response.Outputs.AddRange(result.Outputs);
            return response;
        }
        if (!IsSafeFileName(request.OutputName))
        {
            return new WorkerLinkResponse
            {
                Success = false,
                ExitCode = -1,
                Stderr = "Invalid output file name.",
            };
        }
        if (!IsAllowedExecutable(request.Linker))
        {
            return new WorkerLinkResponse
            {
                Success = false,
                ExitCode = -1,
                Stderr = "Executable is not allowed by this worker.",
            };
        }
        if (!AreArgumentsValid(request.Args) || TotalBytes(request.ObjFiles) > _maxInputBytes)
        {
            return new WorkerLinkResponse
            {
                Success = false,
                ExitCode = -1,
                Stderr = "Worker request exceeds configured limits.",
            };
        }

        var tmpDir = Directory.CreateTempSubdirectory("dotori-worker-link-").FullName;
        try
        {
            // Write all obj files
            var objPaths = new List<string>();
            for (int i = 0; i < request.ObjFiles.Count; i++)
            {
                var objPath = Path.Combine(tmpDir, $"input_{i}.o");
                await File.WriteAllBytesAsync(objPath, request.ObjFiles[i].ToByteArray(),
                    context.CancellationToken);
                objPaths.Add(objPath);
            }

            var outputPath = Path.Combine(tmpDir, request.OutputName);
            var args = RemoteArgumentMapper.MaterializeLink(
                request.Args, objPaths, outputPath);

            logger.LogDebug("Link: {Linker} {Args}", request.Linker, string.Join(" ", args));

            var psi = new ProcessStartInfo(request.Linker)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var responseFile = ResponseFileArguments.Create(args, directory: tmpDir);
            foreach (var argument in responseFile.Arguments)
                psi.ArgumentList.Add(argument);

            using var proc = new Process { StartInfo = psi };
            try
            {
                proc.Start();
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                logger.LogWarning(ex, "Unable to start linker {Linker}", request.Linker);
                return new WorkerLinkResponse
                {
                    Success = false,
                    ExitCode = 127,
                    Stderr = $"Unable to start linker '{request.Linker}': {ex.Message}",
                };
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(context.CancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(context.CancellationToken);
            var exited = await WaitForExitAsync(proc, context.CancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!exited)
            {
                return new WorkerLinkResponse
                {
                    Success = false,
                    ExitCode = -1,
                    Stderr = "Worker process exceeded its execution timeout.",
                };
            }

            byte[] outBytes = proc.ExitCode == 0 && File.Exists(outputPath)
                ? await File.ReadAllBytesAsync(outputPath, context.CancellationToken)
                : [];

            return new WorkerLinkResponse
            {
                Success     = proc.ExitCode == 0,
                ExitCode    = proc.ExitCode,
                OutputBytes = ByteString.CopyFrom(outBytes),
                Stdout      = stdout,
                Stderr      = stderr,
            };
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ }
        }
    }

    public override Task<HealthResponse> Health(
        HealthRequest     request,
        ServerCallContext context)
    {
        var resp = new HealthResponse
        {
            Ok      = true,
            HostOs = RemoteBundleClient.HostOs,
            HostArch = RemoteBundleClient.HostArch,
            UploadedToolchains = _allowUploadedToolchains,
            Version = Assembly.GetExecutingAssembly()
                          .GetName().Version?.ToString() ?? "0.0.0",
        };
        resp.SupportedTargets.AddRange(_supportedTargets.Value);
        return Task.FromResult(resp);
    }

    private static async Task StreamStderrAsync(
        Process                                  proc,
        IServerStreamWriter<WorkerCompileEvent>  stream,
        CancellationToken                        ct)
    {
        while (await proc.StandardError.ReadLineAsync(ct) is { } line)
        {
            await stream.WriteAsync(new WorkerCompileEvent
            {
                Log = new LogLine { Stream = LogLine.Types.Stream.Stderr, Text = line }
            }, ct);
        }
    }

    private static bool IsSafeFileName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Path.GetFileName(value).Equals(value, StringComparison.Ordinal) &&
        !value.Contains("..", StringComparison.Ordinal);

    private bool IsAllowedExecutable(string executable) =>
        _allowedExecutables.Contains(CanonicalExecutableName(executable));

    private bool AreArgumentsValid(IEnumerable<string> args)
    {
        var count = 0;
        foreach (var arg in args)
        {
            if (++count > _maxArguments || arg.Length > 32_768 || arg.IndexOf('\0') >= 0)
                return false;
        }
        return true;
    }

    private static long TotalBytes(IEnumerable<ByteString> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            if (long.MaxValue - total < value.Length) return long.MaxValue;
            total += value.Length;
        }
        return total;
    }

    private async Task<bool> WaitForExitAsync(Process process, CancellationToken requestToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        timeout.CancelAfter(_executionTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            await process.WaitForExitAsync(CancellationToken.None);
            return false;
        }
    }

    private static HashSet<string> ParseAllowedExecutables(string? configured) =>
        (string.IsNullOrWhiteSpace(configured) ? DefaultExecutables : configured.Split(','))
            .Select(value => CanonicalExecutableName(value.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static string CanonicalExecutableName(string executable)
    {
        var fileName = Path.GetFileName(executable);
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static IReadOnlyList<string> ResolveSupportedTargets(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return ToolchainDetector.DetectAvailable()
            .Select(target => ToolchainDetector.Detect(target)?.TargetTriple)
            .Where(triple => !string.IsNullOrWhiteSpace(triple))
            .Select(triple => triple!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
