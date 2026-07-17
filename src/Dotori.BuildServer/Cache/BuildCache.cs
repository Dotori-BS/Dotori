using System.Security.Cryptography;

namespace Dotori.BuildServer.Cache;

/// <summary>
/// Content-addressable cache for compiled object files.
/// Key = SHA-256(source_hash + compiler_args_hash).
/// Stored under <c>~/.dotori/build-cache/</c> by default.
/// </summary>
public sealed class BuildCache
{
    private readonly string _cacheDir;
    private readonly long _maxBytes;
    private readonly TimeSpan? _maxAge;
    private readonly object _pruneLock = new();

    public BuildCache(string? cacheDir = null, long maxBytes = 0, TimeSpan? maxAge = null)
    {
        _cacheDir = cacheDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotori", "build-cache");
        Directory.CreateDirectory(_cacheDir);
        _maxBytes = Math.Max(0, maxBytes);
        _maxAge = maxAge is { } age && age > TimeSpan.Zero ? age : null;
        if (_maxBytes > 0 || _maxAge is not null) Prune();
    }

    /// <summary>Compute cache key from source hash and compiler args.</summary>
    public static string ComputeKey(
        string sourceHash,
        IEnumerable<string> args,
        string compiler = "",
        string targetTriple = "",
        string compilerFingerprint = "",
        string dependencyHash = "")
    {
        // Delimit every field: simple concatenation lets distinct argument lists
        // such as ["-D", "AB"] and ["-DA", "B"] collide.  The compiler and
        // target are also part of an object file's ABI and must not share entries.
        using var stream = new MemoryStream();
        WriteField(stream, "dotori-build-cache-v2");
        WriteField(stream, sourceHash);
        WriteField(stream, compiler);
        WriteField(stream, targetTriple);
        WriteField(stream, compilerFingerprint);
        WriteField(stream, dependencyHash);
        foreach (var arg in args)
            WriteField(stream, arg);

        var bytes = SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
        return Convert.ToHexStringLower(bytes);
    }

    private static void WriteField(Stream stream, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    /// <summary>Try to get a cached object file.</summary>
    public bool TryGet(string key, out byte[] objBytes)
    {
        var path = CachePath(key);
        if (!File.Exists(path)) { objBytes = []; return false; }
        try
        {
            objBytes = File.ReadAllBytes(path);
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
            catch (IOException) { }
            return objBytes.Length > 0;
        }
        catch (IOException)
        {
            objBytes = [];
            return false;
        }
    }

    /// <summary>Store compiled object bytes under the given key.</summary>
    public void Put(string key, byte[] objBytes)
    {
        var path = CachePath(key);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, objBytes);
            File.Move(temp, path, overwrite: true);
            if (_maxBytes > 0 || _maxAge is not null) Prune();
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Removes expired entries, then least-recently-used entries over the size limit.</summary>
    public int Prune()
    {
        lock (_pruneLock)
        {
            var removed = 0;
            var now = DateTime.UtcNow;
            var files = Directory.EnumerateFiles(_cacheDir, "*.o")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .ToList();

            if (_maxAge is { } maxAge)
            {
                foreach (var file in files.Where(file => now - LastUsed(file) > maxAge).ToArray())
                {
                    if (TryDelete(file.FullName)) removed++;
                    files.Remove(file);
                }
            }

            if (_maxBytes > 0)
            {
                var total = files.Sum(file => file.Length);
                foreach (var file in files.OrderBy(LastUsed))
                {
                    if (total <= _maxBytes) break;
                    if (!TryDelete(file.FullName)) continue;
                    total -= file.Length;
                    removed++;
                }
            }
            return removed;
        }
    }

    private static DateTime LastUsed(FileInfo file) =>
        file.LastAccessTimeUtc > file.LastWriteTimeUtc
            ? file.LastAccessTimeUtc
            : file.LastWriteTimeUtc;

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private string CachePath(string key) => Path.Combine(_cacheDir, key + ".o");
}
