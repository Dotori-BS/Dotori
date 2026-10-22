using Grpc.Core;

namespace Dotori.Grpc;

public static class RemoteContentTransfer
{
    public static async Task UploadMissingAsync(
        IEnumerable<BundleFile> files, Func<string, string> localPath,
        Func<BlobQuery, Task<BlobQuery>> findMissing,
        Func<AsyncClientStreamingCall<BlobChunk, BlobReceipt>> upload,
        CancellationToken ct)
    {
        foreach (var batch in files.Select(file => file.Hash).Distinct().Chunk(1000))
        {
            var query = new BlobQuery(); query.Hashes.AddRange(batch);
            var missing = await findMissing(query);
            if (missing.Hashes.Any(hash => !batch.Contains(hash)))
                throw new InvalidDataException("Peer requested content outside the submitted manifest.");
            foreach (var hash in missing.Hashes)
            {
                using var call = upload();
                await RemoteContentStore.SendFileAsync(localPath(hash), hash,
                    chunk => call.RequestStream.WriteAsync(chunk), ct);
                await call.RequestStream.CompleteAsync();
                var receipt = await call.ResponseAsync;
                if (receipt.Hash != hash) throw new InvalidDataException("Incorrect upload receipt.");
            }
        }
    }

    public static async Task FetchMissingAsync(
        IEnumerable<BundleFile> files, RemoteContentStore store,
        Func<BlobQuery, AsyncServerStreamingCall<BlobChunk>> download, CancellationToken ct)
    {
        foreach (var file in files.DistinctBy(file => file.Hash))
        {
            if (File.Exists(store.BlobPath(file.Hash))) continue;
            var query = new BlobQuery(); query.Hashes.Add(file.Hash);
            using var call = download(query);
            var receipt = await store.ReceiveAsync(call.ResponseStream, ct);
            if (receipt.Hash != file.Hash || receipt.Size != file.Size)
                throw new InvalidDataException("Downloaded content differs from the output manifest.");
        }
    }
}
