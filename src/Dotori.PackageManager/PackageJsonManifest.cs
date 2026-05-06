using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotori.PackageManager;

/// <summary>Represents a package.json manifest (npm-compatible).</summary>
public sealed class PackageJsonManifest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; init; }

    /// <summary>Registry package dependencies: key = "@scope/name", value = SemVer constraint.</summary>
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; init; }

    /// <summary>Dev-only dependencies (not installed transitively).</summary>
    [JsonPropertyName("devDependencies")]
    public Dictionary<string, string>? DevDependencies { get; init; }

    /// <summary>Dotori-specific dependency overrides: git/path sources.</summary>
    [JsonPropertyName("dotoriDependencies")]
    public Dictionary<string, DotoriDependencyEntry>? DotoriDependencies { get; init; }

    /// <summary>Dotori C++ build metadata.</summary>
    [JsonPropertyName("dotori")]
    public DotoriMetadata? Dotori { get; init; }

    public static PackageJsonManifest? Load(string projectDir)
    {
        var path = Path.Combine(projectDir, "package.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, PackageJsonContext.Default.PackageJsonManifest);
    }
}

public sealed class DotoriDependencyEntry
{
    [JsonPropertyName("git")]
    public string? Git { get; init; }

    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    [JsonPropertyName("commit")]
    public string? Commit { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("option")]
    public JsonElement? Option { get; init; }
}

public sealed class DotoriMetadata
{
    [JsonPropertyName("exports")]
    public Dictionary<string, string>? Exports { get; init; }
}

[JsonSerializable(typeof(PackageJsonManifest))]
[JsonSerializable(typeof(DotoriDependencyEntry))]
[JsonSerializable(typeof(DotoriMetadata))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, DotoriDependencyEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PackageJsonContext : JsonSerializerContext;
