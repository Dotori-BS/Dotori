using Dotori.BuildServer.Cache;
using Dotori.BuildServer.Workers;
using Dotori.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Dotori.BuildServer.Services;

public sealed partial class CoordinatorService
{
    public override Task<BlobQuery> FindMissingBlobs(BlobQuery request, ServerCallContext context) =>
        Task.FromResult(contentStore.Missing(request));
    public override Task<BlobReceipt> UploadBlob(IAsyncStreamReader<BlobChunk> requestStream, ServerCallContext context) =>
        contentStore.ReceiveAsync(requestStream, context.CancellationToken);
    public override Task DownloadBlobs(BlobQuery request, IServerStreamWriter<BlobChunk> responseStream, ServerCallContext context) =>
        contentStore.SendAsync(request, responseStream, context.CancellationToken);

    private async Task PrepareWorkerAsync(WorkerEntry worker, ExecutionBundle bundle, CancellationToken ct) =>
        await RemoteContentTransfer.UploadMissingAsync(bundle.Toolchain.Files.Concat(bundle.Inputs), contentStore.BlobPath,
            async query => await worker.Client.FindMissingBlobsAsync(query, worker.Headers, cancellationToken: ct),
            () => worker.Client.UploadBlob(worker.Headers, cancellationToken: ct), ct);

    private async Task FetchOutputsAsync(WorkerEntry worker, IEnumerable<BundleFile> outputs, CancellationToken ct) =>
        await RemoteContentTransfer.FetchMissingAsync(outputs, contentStore,
            query => worker.Client.DownloadBlobs(query, worker.Headers, cancellationToken: ct), ct);

    private static void ValidateIdentity(ExecutionBundle bundle)
    {
        if (bundle.Toolchain is null || bundle.Toolchain.Id != RemoteContentStore.ToolchainId(bundle.Toolchain) ||
            bundle.Id != RemoteContentStore.ActionId(bundle))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Bundle manifest digest mismatch."));
    }

    private async Task CompileBundledAsync(CompileRequest request, IServerStreamWriter<CompileEvent> stream, ServerCallContext context)
    {
        ValidateIdentity(request.Bundle);
        var ct = context.CancellationToken;
        var key = BuildCache.ComputeKey(request.Bundle.Id, request.Args, request.Compiler, request.TargetTriple);
        var resultPath = Path.Combine(contentStore.DirectoryPath, "results", key + ".compile");
        if (File.Exists(resultPath))
        {
            var cached = CompileResult.Parser.ParseFrom(await File.ReadAllBytesAsync(resultPath, ct));
            if (cached.Outputs.Count > 0 && cached.Outputs.All(file => File.Exists(contentStore.BlobPath(file.Hash))))
            {
                await stream.WriteAsync(new CompileEvent { Result = cached }, ct);
                return;
            }
        }
        var workers = await workerPool.CandidatesAsync(request.TargetTriple, ct,
            request.Bundle.Toolchain.HostOs, request.Bundle.Toolchain.HostArch);
        foreach (var worker in workers)
        {
            try
            {
                await PrepareWorkerAsync(worker, request.Bundle, ct);
                var forwarded = new WorkerCompileRequest { Compiler = request.Compiler, Bundle = request.Bundle };
                forwarded.Args.AddRange(request.Args);
                WorkerCompileResult? result = null;
                using var call = worker.Client.Compile(forwarded, worker.Headers, cancellationToken: ct);
                await foreach (var message in call.ResponseStream.ReadAllAsync(ct))
                {
                    if (message.Log is not null) await stream.WriteAsync(new CompileEvent { Log = message.Log }, ct);
                    else if (message.Result is not null) result = message.Result;
                }
                if (result is null) continue;
                var response = new CompileResult { Success = result.Success, ExitCode = result.ExitCode };
                response.Outputs.AddRange(result.Outputs);
                if (result.Success)
                {
                    await FetchOutputsAsync(worker, result.Outputs, ct);
                    Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
                    var temporary = resultPath + "." + Guid.NewGuid().ToString("N");
                    try
                    {
                        await File.WriteAllBytesAsync(temporary, response.ToByteArray(), ct);
                        File.Move(temporary, resultPath, overwrite: true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                worker.ReportSuccess();
                await stream.WriteAsync(new CompileEvent { Result = response }, ct);
                return;
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            { worker.ReportTransportFailure(); logger.LogWarning(ex, "Bundled compile transport failed"); }
        }
        await stream.WriteAsync(new CompileEvent { Log = new LogLine { Stream = LogLine.Types.Stream.Stderr,
            Text = "No available worker accepts uploaded toolchains for this host OS/CPU, or bundle transfer failed." } }, ct);
        await stream.WriteAsync(new CompileEvent { Result = new CompileResult { Success = false, ExitCode = -2 } }, ct);
    }

    private async Task<LinkResponse> LinkBundledAsync(LinkRequest request, ServerCallContext context)
    {
        ValidateIdentity(request.Bundle);
        var ct = context.CancellationToken;
        var workers = await workerPool.CandidatesAsync(request.TargetTriple, ct,
            request.Bundle.Toolchain.HostOs, request.Bundle.Toolchain.HostArch);
        foreach (var worker in workers)
        {
            try
            {
                await PrepareWorkerAsync(worker, request.Bundle, ct);
                var forwarded = new WorkerLinkRequest { Linker = request.Linker, Bundle = request.Bundle };
                forwarded.Args.AddRange(request.Args);
                var result = await worker.Client.LinkAsync(forwarded, worker.Headers, cancellationToken: ct);
                if (result.Success) await FetchOutputsAsync(worker, result.Outputs, ct);
                var response = new LinkResponse { Success = result.Success, ExitCode = result.ExitCode,
                    Stdout = result.Stdout, Stderr = result.Stderr };
                response.Outputs.AddRange(result.Outputs);
                worker.ReportSuccess();
                return response;
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            { worker.ReportTransportFailure(); logger.LogWarning(ex, "Bundled link transport failed"); }
        }
        return new LinkResponse { Success = false, ExitCode = -2,
            Stderr = "No available worker accepts uploaded toolchains for this host OS/CPU, or bundle transfer failed." };
    }
}
