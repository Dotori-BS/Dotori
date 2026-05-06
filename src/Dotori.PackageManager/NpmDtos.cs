using System.Text.Json.Serialization;

namespace Dotori.PackageManager;

/// <summary>npm packument — full package metadata with all versions.</summary>
public sealed class NpmPackument
{
    [JsonPropertyName("_id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("dist-tags")]
    public Dictionary<string, string> DistTags { get; init; } = [];

    [JsonPropertyName("versions")]
    public Dictionary<string, NpmVersionManifest> Versions { get; init; } = [];

    [JsonPropertyName("time")]
    public Dictionary<string, string>? Time { get; init; }
}

/// <summary>npm version manifest — metadata for a single package version.</summary>
public sealed class NpmVersionManifest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; init; }

    [JsonPropertyName("dotoriDependencies")]
    public Dictionary<string, DotoriDependencyEntry>? DotoriDependencies { get; init; }

    [JsonPropertyName("dotori")]
    public DotoriMetadata? Dotori { get; init; }

    [JsonPropertyName("dist")]
    public NpmDist? Dist { get; init; }
}

public sealed class NpmDist
{
    [JsonPropertyName("tarball")]
    public string Tarball { get; init; } = "";

    [JsonPropertyName("integrity")]
    public string? Integrity { get; init; }

    [JsonPropertyName("shasum")]
    public string? Shasum { get; init; }
}

/// <summary>npm search result.</summary>
public sealed class NpmSearchResult
{
    [JsonPropertyName("objects")]
    public List<NpmSearchObject> Objects { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public sealed class NpmSearchObject
{
    [JsonPropertyName("package")]
    public NpmSearchPackage? Package { get; init; }
}

public sealed class NpmSearchPackage
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>npm publish body.</summary>
public sealed class NpmPublishBody
{
    [JsonPropertyName("_id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("versions")]
    public Dictionary<string, NpmVersionManifest> Versions { get; init; } = [];

    [JsonPropertyName("_attachments")]
    public Dictionary<string, NpmAttachment> Attachments { get; init; } = [];
}

public sealed class NpmAttachment
{
    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = "application/octet-stream";

    [JsonPropertyName("data")]
    public string Data { get; init; } = "";

    [JsonPropertyName("length")]
    public long Length { get; init; }
}

[JsonSerializable(typeof(NpmPackument))]
[JsonSerializable(typeof(NpmVersionManifest))]
[JsonSerializable(typeof(NpmDist))]
[JsonSerializable(typeof(NpmSearchResult))]
[JsonSerializable(typeof(NpmSearchObject))]
[JsonSerializable(typeof(NpmSearchPackage))]
[JsonSerializable(typeof(NpmPublishBody))]
[JsonSerializable(typeof(NpmAttachment))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, NpmVersionManifest>))]
[JsonSerializable(typeof(Dictionary<string, NpmAttachment>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class NpmDtoContext : JsonSerializerContext;
