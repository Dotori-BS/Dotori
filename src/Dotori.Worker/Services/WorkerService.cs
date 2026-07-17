using System.Diagnostics;
using System.Reflection;
using Dotori.Core.Toolchain;
using Dotori.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Dotori.Worker.Services;

/// <summary>
/// gRPC implementation of <see cref="BuildWorker"/>.
/// Receives compile/link requests, executes the compiler locally,
/// and streams results back to the coordinator.
/// </summary>
public sealed class WorkerService(
    ILogger<WorkerService> logger,
    IConfiguration? configuration = null)
    : BuildWorker.BuildWorkerBase
{
    private static readonly string[] DefaultExecutables =
        ["clang", "clang++", "gcc", "g++", "cl", "link", "lld", "ld", "emcc", "em++"];

    private readonly HashSet<string> _allowedExecutables = ParseAllowedExecutables(
        configuration?["DOTORI_ALLOWED_EXECUTABLES"]);
    private readonly long _maxInputBytes = configuration?.GetValue<long?>("DOTORI_MAX_INPUT_BYTES")
        ?? 64L * 1024 * 1024;
    private readonly int _maxArguments = configuration?.GetValue<int?>("DOTORI_MAX_ARGUMENTS") ?? 4096;
    private readonly TimeSpan _executionTimeout = TimeSpan.FromSeconds(
        configuration?.GetValue<int?>("DOTORI_EXECUTION_TIMEOUT_SECONDS") ?? 300);

    public override async Task Compile(
        WorkerCompileRequest                          request,
        IServerStreamWriter<WorkerCompileEvent>       responseStream,
        ServerCallContext                             context)
    {
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

            // Build final arg list: replace any placeholder with actual paths
            var args = request.Args
                .Select(a => a.Replace("{SOURCE}", $"\"{sourceFile}\"")
                              .Replace("{OUTPUT}", $"\"{objFile}\""))
                .Append($"\"{sourceFile}\"")
                .Append($"-o \"{objFile}\"")
                .ToList();

            var argString = string.Join(" ", args);
            logger.LogDebug("Compile: {Compiler} {Args}", request.Compiler, argString);

            var psi = new ProcessStartInfo(request.Compiler, argString)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

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
            var objArgs    = string.Join(" ", objPaths.Select(p => $"\"{p}\""));
            var argString  = string.Join(" ", request.Args) + $" {objArgs} -o \"{outputPath}\"";

            logger.LogDebug("Link: {Linker} {Args}", request.Linker, argString);

            var psi = new ProcessStartInfo(request.Linker, argString)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

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
        var available = ToolchainDetector.DetectAvailable();
        var resp = new HealthResponse
        {
            Ok      = true,
            Version = Assembly.GetExecutingAssembly()
                          .GetName().Version?.ToString() ?? "0.0.0",
        };
        resp.SupportedTargets.AddRange(available);
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
        _allowedExecutables.Contains(Path.GetFileName(executable));

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
            .Select(value => Path.GetFileName(value.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
