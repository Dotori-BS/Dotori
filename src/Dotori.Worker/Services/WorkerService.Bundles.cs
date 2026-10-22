using System.ComponentModel;
using Dotori.Grpc;
using Grpc.Core;

namespace Dotori.Worker.Services;

public sealed partial class WorkerService
{
    private readonly bool _allowUploadedToolchains = configuration?.GetValue<bool>("DOTORI_ALLOW_UPLOADED_TOOLCHAINS") ?? false;
    private readonly Lazy<RemoteContentStore> _bundleStore = new(() => contentStore ?? new RemoteContentStore(
        configuration?["DOTORI_CONTENT_CACHE_DIR"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotori", "worker-content"),
        configuration?.GetValue<long?>("DOTORI_MAX_BLOB_BYTES") ?? 8L * 1024 * 1024 * 1024));

    private RemoteContentStore BundleStore => _allowUploadedToolchains ? _bundleStore.Value :
        throw new RpcException(new Status(StatusCode.PermissionDenied, "Uploaded toolchains are disabled on this worker."));

    public override Task<BlobQuery> FindMissingBlobs(BlobQuery request, ServerCallContext context) =>
        Task.FromResult(BundleStore.Missing(request));

    public override Task<BlobReceipt> UploadBlob(IAsyncStreamReader<BlobChunk> requestStream, ServerCallContext context) =>
        BundleStore.ReceiveAsync(requestStream, context.CancellationToken);

    public override Task DownloadBlobs(BlobQuery request, IServerStreamWriter<BlobChunk> responseStream, ServerCallContext context) =>
        BundleStore.SendAsync(request, responseStream, context.CancellationToken);

    private async Task<RemoteBundleResult> RunBundleAsync(ExecutionBundle bundle, IEnumerable<string> args, CancellationToken ct)
    {
        try
        {
            if (!_allowUploadedToolchains)
                return new(false, -2, "", "Uploaded toolchains are disabled on this worker.", []);
            if (!IsAllowedExecutable(bundle.Executable))
                return new(false, -2, "", "Bundled executable is not allowed by this worker.", []);
            if (!AreArgumentsValid(args) || bundle.Inputs.Sum(file => file.Size) > _maxInputBytes)
                return new(false, -2, "", "Bundled action inputs exceed worker limits.", []);
            return await new RemoteBundleRunner(BundleStore).RunAsync(bundle, args, _executionTimeout, ct);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or Win32Exception or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Bundled action failed");
            return new(false, -2, "", $"Bundled action failed: {ex.Message}", []);
        }
    }
}
