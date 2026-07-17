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

public enum IncrementalFileState
{
    Unchanged,
    Missing,
    Untracked,
    Modified,
}

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
    private readonly ConcurrentDictionary<string, string> _updates;
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
        _updates = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        return GetFileState(filePath) != IncrementalFileState.Unchanged;
    }

    /// <summary>Returns the precise cache state used by incremental planning.</summary>
    public IncrementalFileState GetFileState(string filePath)
    {
        if (!File.Exists(filePath)) return IncrementalFileState.Missing;
        var hash = ComputeHash(filePath);
        if (hash is null) return IncrementalFileState.Modified;
        if (!_hashes.TryGetValue(filePath, out var stored)) return IncrementalFileState.Untracked;
        return stored == hash ? IncrementalFileState.Unchanged : IncrementalFileState.Modified;
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
        _updates[filePath] = hash;
    }

    public bool IsFingerprintChanged(string outputPath, string? fingerprint) =>
        fingerprint is not null &&
        (!_hashes.TryGetValue(FingerprintKey(outputPath), out var stored) || stored != fingerprint);

    public void RecordFingerprint(string outputPath, string? fingerprint)
    {
        if (fingerprint is null) return;
        _hashes.AddOrUpdate(
            FingerprintKey(outputPath),
            _ => { _dirty = true; return fingerprint; },
            (_, existing) =>
            {
                if (existing != fingerprint) _dirty = true;
                return fingerprint;
            });
        _updates[FingerprintKey(outputPath)] = fingerprint;
    }

    private static string FingerprintKey(string outputPath) =>
        $"action:{Path.GetFullPath(outputPath)}";

    /// <summary>Flush the hash DB to disk if it has been modified.</summary>
    public void Save()
    {
        if (!_dirty) return;

        var lockPath = _dbPath + ".lock";
        using var lockStream = AcquireLock(lockPath);

        // Reload under the lock and apply only this instance's changes. This
        // preserves updates written by another concurrently-running build.
        var snapshot = new Dictionary<string, string>(Load(_dbPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _updates)
            snapshot[key] = value;

        var json = JsonSerializer.Serialize(snapshot, HashDbJsonContext.Default.DictionaryStringString);
        var tempPath = $"{_dbPath}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, _dbPath, overwrite: true);
            _updates.Clear();
            _dirty = false;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public void Dispose() => Save();

    private static FileStream AcquireLock(string lockPath)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 199)
            {
                Thread.Sleep(25);
            }
        }
        throw new IOException($"Unable to acquire incremental cache lock: {lockPath}");
    }

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
