namespace Dotori.PackageManager.Config;

/// <summary>
/// Loads Dotori package manager configuration.
///
/// Priority (highest first):
///   1. Project-level .npmrc
///   2. User-level ~/.npmrc
///   3. ~/.dotori/config.toml
///
/// ~/.dotori/config.toml format:
///   [registry]
///   default = "https://npm.pkg.github.com"
///
///   [scopes]
///   "@myorg" = "https://npm.pkg.github.com"
///
///   [registry."https://npm.pkg.github.com"]
///   token = "ghp_..."
///   upstream = "https://..."    # optional proxy upstream
///
/// .npmrc format (subset):
///   @scope:registry=https://registry.url
///   //registry.url/:_authToken=TOKEN
///   registry=https://default.registry.url
/// </summary>
public static class DotoriConfigManager
{
    private static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotori", "config.toml");

    public static DotoriConfig Load(string? projectDir = null)
    {
        var config = new DotoriConfig();

        if (File.Exists(ConfigPath))
            ParseToml(File.ReadAllText(ConfigPath), config);

        var userNpmrc = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npmrc");
        if (File.Exists(userNpmrc))
            ParseNpmrc(File.ReadAllText(userNpmrc), config);

        if (projectDir is not null)
        {
            var projectNpmrc = Path.Combine(projectDir, ".npmrc");
            if (File.Exists(projectNpmrc))
                ParseNpmrc(File.ReadAllText(projectNpmrc), config);
        }

        return config;
    }

    public static void Save(DotoriConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, SerializeToml(config));
    }

    public static void SetToken(string registryUrl, string token)
    {
        var config = Load();
        if (!config.Registries.TryGetValue(registryUrl, out var reg))
        {
            reg = new RegistryConfig { Url = registryUrl };
            config.Registries[registryUrl] = reg;
        }
        reg.Token = token;
        Save(config);
    }

    public static void RemoveToken(string registryUrl)
    {
        var config = Load();
        if (config.Registries.TryGetValue(registryUrl, out var reg))
        {
            reg.Token = null;
            Save(config);
        }
    }

    // ─── TOML parser ─────────────────────────────────────────────────────────

    internal static void ParseToml(string text, DotoriConfig config)
    {
        string? currentSection = null;
        string? currentRegistryUrl = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith('['))
            {
                var header = line.TrimStart('[').TrimEnd(']').Trim();
                if (header == "registry")
                {
                    currentSection = "registry-default";
                    currentRegistryUrl = null;
                }
                else if (header.StartsWith("registry.\"") && header.EndsWith('"'))
                {
                    currentSection = "registry-entry";
                    currentRegistryUrl = header["registry.\"".Length..^1];
                    if (!config.Registries.ContainsKey(currentRegistryUrl))
                        config.Registries[currentRegistryUrl] = new RegistryConfig { Url = currentRegistryUrl };
                }
                else if (header == "scopes")
                {
                    currentSection = "scopes";
                    currentRegistryUrl = null;
                }
                else
                {
                    currentSection = null;
                    currentRegistryUrl = null;
                }
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = Unquote(line[(eq + 1)..].Trim());

            switch (currentSection)
            {
                case "registry-default" when key == "default":
                    config.DefaultRegistry = val;
                    break;
                case "registry-entry" when currentRegistryUrl is not null:
                    var reg = config.Registries[currentRegistryUrl];
                    switch (key)
                    {
                        case "url":      reg.Url = val;      break;
                        case "token":    reg.Token = val;    break;
                        case "upstream": reg.Upstream = val; break;
                    }
                    break;
                case "scopes":
                    // "@myorg" = "https://..."
                    var scope = Unquote(key);
                    config.ScopeMap[scope] = val;
                    break;
            }
        }
    }

    // ─── .npmrc parser ───────────────────────────────────────────────────────

    internal static void ParseNpmrc(string text, DotoriConfig config)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || string.IsNullOrEmpty(line)) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            // @scope:registry=https://...
            if (key.StartsWith('@') && key.EndsWith(":registry"))
            {
                var scope = key[..^":registry".Length];
                config.ScopeMap[scope] = val;
                if (!config.Registries.ContainsKey(val))
                    config.Registries[val] = new RegistryConfig { Url = val };
                continue;
            }

            // registry=https://...  (default)
            if (key == "registry")
            {
                config.DefaultRegistry = val;
                continue;
            }

            // //registry.host/:_authToken=TOKEN
            if (key.StartsWith("//") && key.EndsWith("/:_authToken"))
            {
                var host = key["//".Length..^"/:_authToken".Length];
                // Reconstruct URL — try https first
                var registryUrl = $"https://{host}";
                // Find existing entry with matching host regardless of trailing slash
                var existing = config.Registries.Keys.FirstOrDefault(k =>
                    k.Contains(host, StringComparison.OrdinalIgnoreCase));
                var urlKey = existing ?? registryUrl;
                if (!config.Registries.TryGetValue(urlKey, out var reg))
                {
                    reg = new RegistryConfig { Url = urlKey };
                    config.Registries[urlKey] = reg;
                }
                reg.Token = val;
            }
        }
    }

    // ─── TOML serializer ─────────────────────────────────────────────────────

    private static string SerializeToml(DotoriConfig config)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[registry]");
        sb.AppendLine($"default = \"{config.DefaultRegistry}\"");

        if (config.ScopeMap.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[scopes]");
            foreach (var (scope, url) in config.ScopeMap)
                sb.AppendLine($"\"{scope}\" = \"{url}\"");
        }

        foreach (var (url, reg) in config.Registries)
        {
            sb.AppendLine();
            sb.AppendLine($"[registry.\"{url}\"]");
            if (reg.Token is not null)
                sb.AppendLine($"token = \"{reg.Token}\"");
            if (reg.Upstream is not null)
                sb.AppendLine($"upstream = \"{reg.Upstream}\"");
        }

        return sb.ToString();
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }
}
