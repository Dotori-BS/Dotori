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
public sealed class WorkerService(ILogger<WorkerService> logger)
    : BuildWorker.BuildWorkerBase
{
    public override async Task Compile(
        WorkerCompileRequest                          request,
        IServerStreamWriter<WorkerCompileEvent>       responseStream,
        ServerCallContext                             context)
    {
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

            await proc.WaitForExitAsync(context.CancellationToken);
            var stdout = await stdoutTask;
            await stderrTask;

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
            await proc.WaitForExitAsync(context.CancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

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
}
