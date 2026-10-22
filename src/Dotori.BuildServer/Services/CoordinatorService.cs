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
public sealed partial class CoordinatorService(
    WorkerPool              workerPool,
    BuildCache              cache,
    ILogger<CoordinatorService> logger,
    RemoteContentStore contentStore)
    : BuildCoordinator.BuildCoordinatorBase
{
    public override async Task Compile(
        CompileRequest                          request,
        IServerStreamWriter<CompileEvent>       responseStream,
        ServerCallContext                       context)
    {
        if (request.Bundle is not null)
        {
            await CompileBundledAsync(request, responseStream, context);
            return;
        }
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

        // ── 2. Try healthy workers in round-robin order ───────────────
        var workers = await workerPool.CandidatesAsync(
            request.TargetTriple, context.CancellationToken);
        if (workers.Count == 0)
        {
            await responseStream.WriteAsync(new CompileEvent
            {
                Result = new CompileResult { Success = false, ExitCode = -1 }
            });
            logger.LogError("No workers support target {Target} for compile job.", request.TargetTriple);
            return;
        }

        var workerRequest = new WorkerCompileRequest
        {
            Compiler    = request.Compiler,
            SourceBytes = request.SourceBytes,
            SourcePath  = request.SourcePath,
        };
        workerRequest.Args.AddRange(request.Args);

        WorkerCompileResult? finalResult = null;
        foreach (var worker in workers)
        {
            try
            {
                using var call = worker.Client.Compile(workerRequest, worker.Headers,
                    cancellationToken: context.CancellationToken);
                await foreach (var ev in call.ResponseStream.ReadAllAsync(context.CancellationToken))
                {
                    if (ev.PayloadCase == WorkerCompileEvent.PayloadOneofCase.Log)
                        await responseStream.WriteAsync(new CompileEvent { Log = ev.Log });
                    else if (ev.PayloadCase == WorkerCompileEvent.PayloadOneofCase.Result)
                        finalResult = ev.Result;
                }
                if (finalResult is not null)
                {
                    if (!finalResult.Success && finalResult.ExitCode == -1)
                    {
                        worker.ReportTransportFailure();
                        logger.LogWarning(
                            "Worker {Worker} rejected compile job; retrying.", worker.Address);
                        finalResult = null;
                        continue;
                    }
                    worker.ReportSuccess();
                    break;
                }
                worker.ReportTransportFailure();
                logger.LogWarning("Worker {Worker} returned no compile result; retrying.", worker.Address);
            }
            catch (RpcException ex) when (
                ex.StatusCode != StatusCode.Cancelled || !context.CancellationToken.IsCancellationRequested)
            {
                worker.ReportTransportFailure();
                logger.LogWarning(ex, "Worker {Worker} compile transport failed; retrying.", worker.Address);
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
        if (request.Bundle is not null) return await LinkBundledAsync(request, context);
        var workers = await workerPool.CandidatesAsync(
            request.TargetTriple, context.CancellationToken);
        if (workers.Count == 0)
        {
            logger.LogError("No workers support target {Target} for link job.", request.TargetTriple);
            return new LinkResponse { Success = false, ExitCode = -1 };
        }

        var workerRequest = new WorkerLinkRequest
        {
            Linker     = request.Linker,
            OutputName = request.OutputName,
        };
        workerRequest.Args.AddRange(request.Args);
        workerRequest.ObjFiles.AddRange(request.ObjFiles);

        foreach (var worker in workers)
        {
            try
            {
                var response = await worker.Client.LinkAsync(workerRequest, worker.Headers,
                    cancellationToken: context.CancellationToken);
                if (!response.Success && response.ExitCode == -1)
                {
                    worker.ReportTransportFailure();
                    logger.LogWarning(
                        "Worker {Worker} rejected link job; retrying.", worker.Address);
                    continue;
                }
                worker.ReportSuccess();
                return new LinkResponse
                {
                    Success = response.Success,
                    ExitCode = response.ExitCode,
                    OutputBytes = response.OutputBytes,
                    Stdout = response.Stdout,
                    Stderr = response.Stderr,
                };
            }
            catch (RpcException ex) when (
                ex.StatusCode != StatusCode.Cancelled || !context.CancellationToken.IsCancellationRequested)
            {
                worker.ReportTransportFailure();
                logger.LogWarning(ex, "Worker {Worker} link transport failed; retrying.", worker.Address);
            }
        }
        return new LinkResponse
        {
            Success = false,
            ExitCode = -1,
            Stderr = "All remote workers failed or are unavailable.",
        };
    }

    public override async Task<ListWorkersResponse> ListWorkers(
        ListWorkersRequest request,
        ServerCallContext  context)
    {
        await workerPool.RefreshCapabilitiesAsync(context.CancellationToken);
        var resp = new ListWorkersResponse();
        foreach (var w in workerPool.All)
        {
            var info = new WorkerInfo
            {
                Id      = w.Address,
                Address = w.Address,
            };
            info.Targets.AddRange(w.SupportedTargets);
            resp.Workers.Add(info);
        }
        return resp;
    }
}
