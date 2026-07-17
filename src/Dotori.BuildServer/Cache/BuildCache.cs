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

    public BuildCache(string? cacheDir = null)
    {
        _cacheDir = cacheDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotori", "build-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Compute cache key from source hash and compiler args.</summary>
    public static string ComputeKey(
        string sourceHash,
        IEnumerable<string> args,
        string compiler = "",
        string targetTriple = "")
    {
        // Delimit every field: simple concatenation lets distinct argument lists
        // such as ["-D", "AB"] and ["-DA", "B"] collide.  The compiler and
        // target are also part of an object file's ABI and must not share entries.
        using var stream = new MemoryStream();
        WriteField(stream, "dotori-build-cache-v2");
        WriteField(stream, sourceHash);
        WriteField(stream, compiler);
        WriteField(stream, targetTriple);
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
        objBytes = File.ReadAllBytes(path);
        return true;
    }

    /// <summary>Store compiled object bytes under the given key.</summary>
    public void Put(string key, byte[] objBytes)
    {
        File.WriteAllBytes(CachePath(key), objBytes);
    }

    private string CachePath(string key) => Path.Combine(_cacheDir, key + ".o");
}
