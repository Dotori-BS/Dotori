using System.Text.Json.Serialization;

namespace Dotori.PackageManager;

/// <summary>A single resolved package entry in the lock file.</summary>
public sealed class LockEntry
{
    /// <summary>Resolved version string.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Tarball URL, git+url, or path: URI.</summary>
    [JsonPropertyName("resolved")]
    public required string Resolved { get; init; }

    /// <summary>SRI integrity string (sha512-...) or sha256: hash for git deps.</summary>
    [JsonPropertyName("integrity")]
    public string? Integrity { get; init; }

    /// <summary>Registry URL this package was fetched from. Null for path/git deps.</summary>
    [JsonPropertyName("registry")]
    public string? Registry { get; init; }

    /// <summary>True when this is a path (local) dependency link.</summary>
    [JsonPropertyName("link")]
    public bool Link { get; init; }

    /// <summary>Direct dependencies: key = "@scope/name", value = SemVer constraint.</summary>
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; init; } = [];
}

/// <summary>The full lock file model (v2, JSON).</summary>
public sealed class LockFile
{
    [JsonPropertyName("lockVersion")]
    public int LockVersion { get; set; } = 2;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>key = "@scope/name@version"</summary>
    [JsonPropertyName("packages")]
    public Dictionary<string, LockEntry> Packages { get; init; } = new(StringComparer.Ordinal);
}
