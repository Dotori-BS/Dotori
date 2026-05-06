using Dotori.PackageManager.Config;

namespace Dotori.PackageManager;

/// <summary>
/// PubGrub IPackageSource backed by npm registries (multi-registry, scope-routed).
/// Falls back to the existing lock file when offline.
/// </summary>
internal sealed class RegistryPackageSource : IPackageSource
{
    private readonly LockFile _existingLock;
    private readonly DotoriConfig _config;
    private readonly CancellationToken _ct;

    // git-only deps: name → (url, tagOrCommit)
    private readonly Dictionary<string, (string url, string tagOrCommit)> _gitInfo = new();

    // caches
    private readonly Dictionary<string, List<SemanticVersion>> _versionCache
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, VersionConstraint>> _depCache
        = new();
    // packument cache: key = scopedName
    private readonly Dictionary<string, NpmPackument?> _packumentCache
        = new(StringComparer.OrdinalIgnoreCase);

    public RegistryPackageSource(LockFile existingLock, DotoriConfig config, CancellationToken ct)
    {
        _existingLock = existingLock;
        _config = config;
        _ct = ct;
    }

    public void RegisterGit(string name, string url, string tagOrCommit) =>
        _gitInfo[name] = (url, tagOrCommit);

    public bool TryGetGitInfo(string name, out string? url, out string? tagOrCommit)
    {
        if (_gitInfo.TryGetValue(name, out var info))
        {
            url = info.url;
            tagOrCommit = info.tagOrCommit;
            return true;
        }
        url = tagOrCommit = null;
        return false;
    }

    public bool IsRegistryPackage(string name) =>
        name.StartsWith('@') && !_gitInfo.ContainsKey(name);

    public string GetRegistryUrl(string name) => _config.ResolveRegistryUrl(name);

    public async Task<IReadOnlyList<SemanticVersion>> GetVersionsAsync(
        string package, CancellationToken ct)
    {
        if (_versionCache.TryGetValue(package, out var cached))
            return cached;

        var versions = new List<SemanticVersion>();

        // 1. From existing lock
        foreach (var (key, entry) in _existingLock.Packages)
        {
            var atIdx = key.LastIndexOf('@', key.Length - 1, key.Length - 1);
            if (atIdx <= 0) continue;
            var entryName = key[..atIdx];
            if (!entryName.Equals(package, StringComparison.OrdinalIgnoreCase)) continue;
            if (SemanticVersion.TryParse(entry.Version, out var v) && !versions.Contains(v))
                versions.Add(v);
        }

        // 2. Git dep: single fixed version
        if (_gitInfo.TryGetValue(package, out var gitInfo))
        {
            var tagOrCommit = gitInfo.tagOrCommit;
            var vstr = tagOrCommit.StartsWith('v')
                ? tagOrCommit[1..]
                : tagOrCommit[..Math.Min(8, tagOrCommit.Length)];
            if (SemanticVersion.TryParse(vstr, out var gitVer) && !versions.Contains(gitVer))
                versions.Add(gitVer);
        }
        // 3. npm scoped package → fetch packument
        else if (package.StartsWith('@'))
        {
            var packument = await GetCachedPackumentAsync(package, ct);
            if (packument is not null)
            {
                foreach (var (ver, manifest) in packument.Versions)
                {
                    if (SemanticVersion.TryParse(ver, out var rv) && !versions.Contains(rv))
                        versions.Add(rv);
                }
            }
        }

        versions.Sort((a, b) => b.CompareTo(a));
        _versionCache[package] = versions;
        return versions;
    }

    public async Task<IReadOnlyDictionary<string, VersionConstraint>> GetDependenciesAsync(
        string package, SemanticVersion version, CancellationToken ct)
    {
        var key = $"{package}@{version}";
        if (_depCache.TryGetValue(key, out var cached))
            return cached;

        // 1. From lock file
        if (_existingLock.Packages.TryGetValue(key, out var lockEntry)
            && lockEntry.Dependencies.Count > 0)
        {
            var deps = lockEntry.Dependencies
                .ToDictionary(kv => kv.Key, kv => VersionConstraint.Parse(kv.Value));
            _depCache[key] = deps;
            return deps;
        }

        // 2. From packument
        if (package.StartsWith('@'))
        {
            var packument = await GetCachedPackumentAsync(package, ct);
            var versionStr = version.ToString();
            if (packument is not null && packument.Versions.TryGetValue(versionStr, out var manifest)
                && manifest.Dependencies is { Count: > 0 })
            {
                var deps = manifest.Dependencies
                    .ToDictionary(kv => kv.Key, kv => VersionConstraint.Parse(kv.Value));
                _depCache[key] = deps;
                return deps;
            }
        }

        var empty = new Dictionary<string, VersionConstraint>();
        _depCache[key] = empty;
        return empty;
    }

    public async Task<NpmVersionManifest?> GetVersionManifestAsync(
        string package, SemanticVersion version, CancellationToken ct)
    {
        var packument = await GetCachedPackumentAsync(package, ct);
        return packument?.Versions.GetValueOrDefault(version.ToString());
    }

    private async Task<NpmPackument?> GetCachedPackumentAsync(
        string scopedName, CancellationToken ct)
    {
        if (_packumentCache.TryGetValue(scopedName, out var cached))
            return cached;

        NpmPackument? packument = null;
        try
        {
            using var client = NpmRegistryClient.ForPackage(scopedName, _config);
            packument = await client.GetPackumentAsync(scopedName, ct);
        }
        catch { /* offline fallback */ }

        _packumentCache[scopedName] = packument;
        return packument;
    }
}
