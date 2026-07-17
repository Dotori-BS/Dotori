using Dotori.BuildServer.Cache;
using Dotori.BuildServer.Workers;
using Dotori.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Dotori.BuildServer.Services;

/// <summary>
/// gRPC implementation of <see cref="BuildCoordinator"/>.
/// Handles cache lookup, worker dispatch, and result streaming back to the CLI.
/// </summary>
public sealed class CoordinatorService(
    WorkerPool              workerPool,
    BuildCache              cache,
    ILogger<CoordinatorService> logger)
    : BuildCoordinator.BuildCoordinatorBase
{
    public override async Task Compile(
        CompileRequest                          request,
        IServerStreamWriter<CompileEvent>       responseStream,
        ServerCallContext                       context)
    {
        // ── 1. Cache hit? ──────────────────────────────────────────────
        var cacheKey = BuildCache.ComputeKey(
            request.SourceHash, request.Args, request.Compiler, request.TargetTriple,
            request.CompilerFingerprint, request.DependencyHash);
        if (cache.TryGet(cacheKey, out var cachedObj))
        {
            logger.LogDebug("Cache hit: {Key}", cacheKey);
            await responseStream.WriteAsync(new CompileEvent
            {
                Result = new CompileResult
                {
                    Success  = true,
                    ExitCode = 0,
                    ObjBytes = ByteString.CopyFrom(cachedObj),
                    ObjHash  = request.SourceHash,
                }
            });
            return;
        }

        // ── 2. Pick a worker ───────────────────────────────────────────
        var worker = workerPool.NextWorker();
        if (worker is null)
        {
            await responseStream.WriteAsync(new CompileEvent
            {
                Result = new CompileResult { Success = false, ExitCode = -1 }
            });
            logger.LogError("No workers available for compile job.");
            return;
        }

        // ── 3. Forward to worker, stream events back ───────────────────
        var workerRequest = new WorkerCompileRequest
        {
            Compiler    = request.Compiler,
            SourceBytes = request.SourceBytes,
            SourcePath  = request.SourcePath,
        };
        workerRequest.Args.AddRange(request.Args);

        using var call = worker.Client.Compile(workerRequest, worker.Headers,
            cancellationToken: context.CancellationToken);

        WorkerCompileResult? finalResult = null;

        await foreach (var ev in call.ResponseStream.ReadAllAsync(context.CancellationToken))
        {
            if (ev.PayloadCase == WorkerCompileEvent.PayloadOneofCase.Log)
            {
                await responseStream.WriteAsync(new CompileEvent { Log = ev.Log });
            }
            else if (ev.PayloadCase == WorkerCompileEvent.PayloadOneofCase.Result)
            {
                finalResult = ev.Result;
            }
        }

        if (finalResult is null)
        {
            await responseStream.WriteAsync(new CompileEvent
            {
                Result = new CompileResult { Success = false, ExitCode = -1 }
            });
            return;
        }

        // ── 4. Cache successful result ─────────────────────────────────
        if (finalResult.Success && finalResult.ObjBytes.Length > 0)
            cache.Put(cacheKey, finalResult.ObjBytes.ToByteArray());

        await responseStream.WriteAsync(new CompileEvent
        {
            Result = new CompileResult
            {
                Success  = finalResult.Success,
                ExitCode = finalResult.ExitCode,
                ObjBytes = finalResult.ObjBytes,
                ObjHash  = cacheKey,
            }
        });
    }

    public override async Task<LinkResponse> Link(
        LinkRequest       request,
        ServerCallContext context)
    {
        var worker = workerPool.NextWorker();
        if (worker is null)
        {
            logger.LogError("No workers available for link job.");
            return new LinkResponse { Success = false, ExitCode = -1 };
        }

        var workerRequest = new WorkerLinkRequest
        {
            Linker     = request.Linker,
            OutputName = request.OutputName,
        };
        workerRequest.Args.AddRange(request.Args);
        workerRequest.ObjFiles.AddRange(request.ObjFiles);

        var response = await worker.Client.LinkAsync(workerRequest, worker.Headers,
            cancellationToken: context.CancellationToken);

        return new LinkResponse
        {
            Success     = response.Success,
            ExitCode    = response.ExitCode,
            OutputBytes = response.OutputBytes,
            Stdout      = response.Stdout,
            Stderr      = response.Stderr,
        };
    }

    public override Task<ListWorkersResponse> ListWorkers(
        ListWorkersRequest request,
        ServerCallContext  context)
    {
        var resp = new ListWorkersResponse();
        foreach (var w in workerPool.All)
        {
            resp.Workers.Add(new WorkerInfo
            {
                Id      = w.Address,
                Address = w.Address,
            });
        }
        return Task.FromResult(resp);
    }
}
