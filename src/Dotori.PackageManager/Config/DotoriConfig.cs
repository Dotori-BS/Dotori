namespace Dotori.PackageManager.Config;

public sealed class RegistryConfig
{
    public string  Url      { get; set; } = "";
    public string? Token    { get; set; }
    /// <summary>Proxy upstream URL (for self-hosted proxying registries).</summary>
    public string? Upstream { get; set; }
}

public sealed class DotoriConfig
{
    public string DefaultRegistry { get; set; } = "https://registry.npmjs.org";

    /// <summary>key = registry URL (e.g. "https://npm.pkg.github.com")</summary>
    public Dictionary<string, RegistryConfig> Registries { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>key = "@scope" (e.g. "@myorg"), value = registry URL</summary>
    public Dictionary<string, string> ScopeMap { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve the registry URL for a given scoped package name.</summary>
    public string ResolveRegistryUrl(string scopedName)
    {
        if (scopedName.StartsWith('@'))
        {
            var slash = scopedName.IndexOf('/');
            var scope = slash > 0 ? scopedName[..slash] : scopedName;
            if (ScopeMap.TryGetValue(scope, out var url))
                return url;
        }
        return DefaultRegistry;
    }

    /// <summary>Get RegistryConfig for a given URL (or default registry).</summary>
    public RegistryConfig GetRegistry(string? url = null)
    {
        var key = url ?? DefaultRegistry;
        if (Registries.TryGetValue(key, out var cfg))
            return cfg;
        return new RegistryConfig { Url = key };
    }
}
