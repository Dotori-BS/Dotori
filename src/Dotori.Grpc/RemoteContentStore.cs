using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;

namespace Dotori.Grpc;

/// <summary>Verified, chunked, content-addressed storage shared by coordinator and worker.</summary>
public sealed class RemoteContentStore
{
    public const int ChunkSize = 1024 * 1024;
    private readonly string _directory;
    private readonly long _maxBlobBytes;
    public string DirectoryPath => _directory;

    public RemoteContentStore(string directory, long maxBlobBytes = 8L * 1024 * 1024 * 1024)
    {
        _directory = Path.GetFullPath(directory);
        _maxBlobBytes = maxBlobBytes;
        Directory.CreateDirectory(_directory);
    }

    public static bool IsHash(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public string BlobPath(string hash)
    {
        if (!IsHash(hash)) throw new InvalidDataException("Invalid SHA-256 content identifier.");
        return Path.Combine(_directory, "blobs", hash[..2], hash);
    }

    public BlobQuery Missing(BlobQuery query)
    {
        if (query.Hashes.Count > 1000) throw new InvalidDataException("At most 1000 hashes per query.");
        var result = new BlobQuery();
        result.Hashes.AddRange(query.Hashes.Distinct().Where(hash => !File.Exists(BlobPath(hash))));
        return result;
    }

    public async Task<BlobReceipt> ReceiveAsync(IAsyncStreamReader<BlobChunk> reader, CancellationToken ct)
    {
        var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".upload");
        string? identifier = null;
        long size = 0;
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = File.Create(temporary))
            {
                while (await reader.MoveNext(ct))
                {
                    var chunk = reader.Current;
                    _ = BlobPath(chunk.Hash);
                    identifier ??= chunk.Hash;
                    if (identifier != chunk.Hash || chunk.Data.Length > ChunkSize ||
                        chunk.Data.Length > _maxBlobBytes - size)
                        throw new InvalidDataException("Invalid or oversized content upload.");
                    size += chunk.Data.Length;
                    hasher.AppendData(chunk.Data.Span);
                    await output.WriteAsync(chunk.Data.Memory, ct);
                }
            }
            if (identifier is null || Convert.ToHexStringLower(hasher.GetHashAndReset()) != identifier)
                throw new InvalidDataException("Uploaded content does not match its SHA-256 identifier.");
            var destination = BlobPath(identifier);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination)) { }
            return new BlobReceipt { Hash = identifier, Size = size };
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task SendAsync(BlobQuery query, IServerStreamWriter<BlobChunk> writer, CancellationToken ct)
    {
        if (query.Hashes.Count > 1000) throw new InvalidDataException("At most 1000 hashes per query.");
        foreach (var hash in query.Hashes.Distinct())
            await SendFileAsync(BlobPath(hash), hash, chunk => writer.WriteAsync(chunk, ct), ct);
    }

    public static async Task SendFileAsync(
        string path, string hash, Func<BlobChunk, Task> write, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[ChunkSize];
        int count;
        var sent = false;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await write(new BlobChunk { Hash = hash, Data = ByteString.CopyFrom(buffer, 0, count) });
            sent = true;
        }
        if (!sent) await write(new BlobChunk { Hash = hash });
    }

    public async Task<string> ImportFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > _maxBlobBytes) throw new InvalidDataException("Content exceeds the blob limit.");
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        var destination = BlobPath(hash);
        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N");
            try
            {
                stream.Position = 0;
                await using (var output = File.Create(temporary)) await stream.CopyToAsync(output, ct);
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return hash;
    }

    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Contains('\0') || relative.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Bundle paths must be safe relative paths.");
        if (OperatingSystem.IsWindows() && relative.Split('/').Any(part =>
            part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            IsWindowsDevice(part.Split('.')[0])))
            throw new InvalidDataException("Bundle path uses a reserved Windows file name.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Bundle path escapes its root.");
        return path;
    }

    private static bool IsWindowsDevice(string name)
    {
        name = name.ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
                name[3] is >= '1' and <= '9');
    }

    public static string ToolchainId(ToolchainBundle bundle)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, bundle.HostOs); Add(hash, bundle.HostArch);
        AddFiles(hash, bundle.Files);
        foreach (var item in bundle.Environment.OrderBy(item => item.Key, StringComparer.Ordinal))
        { Add(hash, item.Key); Add(hash, item.Value); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string ActionId(ExecutionBundle bundle)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, bundle.Toolchain.Id); Add(hash, bundle.Executable);
        Add(hash, bundle.WorkingDirectory); Add(hash, bundle.OutputPath);
        AddFiles(hash, bundle.Inputs);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AddFiles(IncrementalHash hash, IEnumerable<BundleFile> files)
    {
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        { Add(hash, file.Path); Add(hash, file.Hash); Add(hash, file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)); Add(hash, file.Executable ? "1" : "0"); }
    }

    private static void Add(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }
}
