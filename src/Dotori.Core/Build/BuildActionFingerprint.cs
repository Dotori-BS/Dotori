using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace Dotori.Core.Build;

internal static class BuildActionFingerprint
{
    private static readonly ConcurrentDictionary<string, string> ExecutableCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Compute(
        string executable,
        IReadOnlyList<string> args,
        IReadOnlyList<string>? dependencyFiles = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, ResolveExecutableFingerprint(executable));
        foreach (var arg in args)
            Append(hash, arg);
        if (dependencyFiles is not null)
        {
            foreach (var dependency in dependencyFiles
                         .Select(Path.GetFullPath)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                Append(hash, dependency);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static string ResolveExecutableFingerprint(string executable)
    {
        var path = ResolveExecutable(executable);
        if (path is null) return executable;
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        var cacheKey = $"{fullPath}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        return ExecutableCache.GetOrAdd(cacheKey, _ =>
        {
            using var stream = File.OpenRead(fullPath);
            return $"{fullPath}:{Convert.ToHexStringLower(SHA256.HashData(stream))}";
        });
    }

    private static string? ResolveExecutable(string executable)
    {
        if (File.Exists(executable)) return executable;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
