using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotori.Core;

namespace Dotori.Core.Build;

// NativeAOT-safe serialization context for the hash dictionary
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class HashDbJsonContext : JsonSerializerContext { }

/// <summary>
/// Hash-based incremental build checker.
/// Stores file hashes in <c>.dotori-cache/hashes.db</c> (JSON format).
/// Thread-safe: multiple compile jobs may call <see cref="IsChanged"/> and
/// <see cref="Record"/> concurrently.
/// </summary>
public sealed class IncrementalChecker : IDisposable
{
    private readonly string _dbPath;
    private readonly ConcurrentDictionary<string, string> _hashes;  // path → SHA256 hex
    private volatile bool _dirty;

    public IncrementalChecker(string projectDir, string? scope = null)
    {
        var cacheDir = Path.Combine(projectDir, DotoriConstants.CacheDir);
        Directory.CreateDirectory(cacheDir);
        var dbName = string.IsNullOrWhiteSpace(scope)
            ? DotoriConstants.HashDbFileName
            : $"hashes.{SanitizeScope(scope)}.db";
        _dbPath  = Path.Combine(cacheDir, dbName);
        _hashes  = Load(_dbPath);
        _dirty   = false;
    }

    private static string SanitizeScope(string scope) => string.Concat(
        scope.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    /// <summary>
    /// Returns true if the file has changed since it was last recorded.
    /// A file is considered changed if it doesn't exist in the DB or its hash differs.
    /// </summary>
    public bool IsChanged(string filePath)
    {
        var hash = ComputeHash(filePath);
        if (hash is null) return true;  // file doesn't exist — treat as changed

        return !_hashes.TryGetValue(filePath, out var stored) || stored != hash;
    }

    /// <summary>
    /// Record the current hash of a file (call after successful compilation).
    /// </summary>
    public void Record(string filePath)
    {
        var hash = ComputeHash(filePath);
        if (hash is null) return;

        _hashes.AddOrUpdate(
            filePath,
            addValueFactory: _ => { _dirty = true; return hash; },
            updateValueFactory: (_, existing) =>
            {
                if (existing != hash) _dirty = true;
                return hash;
            });
    }

    /// <summary>Flush the hash DB to disk if it has been modified.</summary>
    public void Save()
    {
        if (!_dirty) return;
        // Snapshot to a plain Dictionary for serialization
        var snapshot = new Dictionary<string, string>(_hashes, StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(snapshot, HashDbJsonContext.Default.DictionaryStringString);
        File.WriteAllText(_dbPath, json, Encoding.UTF8);
        _dirty = false;
    }

    public void Dispose() => Save();

    private static ConcurrentDictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            return new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json   = File.ReadAllText(path, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize(json, HashDbJsonContext.Default.DictionaryStringString);
            return loaded is not null
                ? new ConcurrentDictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase)
                : new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ComputeHash(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var stream = File.OpenRead(filePath);
            var bytes = SHA256.HashData(stream);
            return Convert.ToHexStringLower(bytes);
        }
        catch
        {
            return null;
        }
    }
}
