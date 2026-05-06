using Dotori.Core;
using Dotori.Core.Parsing;
using Dotori.PackageManager.Config;

namespace Dotori.PackageManager;

/// <summary>
/// Resolves package dependencies using PubGrub and produces an updated lock file.
/// Reads dependencies from package.json (preferred) or .dotori dependencies block (legacy).
/// </summary>
public static class DependencyResolver
{
    /// <summary>
    /// Resolve all non-path dependencies and return an updated lock file.
    /// </summary>
    public static async Task<LockFile> ResolveAsync(
        string projectDir,
        LockFile existingLock,
        ProjectDecl? project = null,
        CancellationToken ct = default)
    {
        var depsRoot = Path.Combine(projectDir, DotoriConstants.DepsDir);
        var config = DotoriConfigManager.Load(projectDir);

        // Collect root dependencies: package.json preferred, .dotori fallback
        var manifest = PackageJsonManifest.Load(projectDir);
        var rootDeps = manifest is not null
            ? CollectRootDependencies(manifest)
            : project is not null
                ? CollectRootDependencies(project)
                : new Dictionary<string, VersionConstraint>();

        if (rootDeps.Count == 0)
            return existingLock;

        var source = new RegistryPackageSource(existingLock, config, ct);

        // Register git deps from dotoriDependencies or .dotori block
        if (manifest?.DotoriDependencies is not null)
            RegisterGitDependencies(manifest.DotoriDependencies, source);
        else if (project is not null)
            RegisterGitDependencies(project, source);

        var solver = new PubGrubSolver(source);
        var solved = await solver.SolveAsync(rootDeps, ct);

        var newLock = new LockFile
        {
            Name    = manifest?.Name,
            Version = manifest?.Version,
        };

        foreach (var (name, version) in solved)
        {
            ct.ThrowIfCancellationRequested();

            var lockKey = $"{name}@{version}";

            // Re-use existing entry if unchanged
            if (existingLock.Packages.TryGetValue(lockKey, out var existing))
            {
                newLock.Packages[lockKey] = existing;
                continue;
            }

            if (source.TryGetGitInfo(name, out var gitUrl, out var tagOrCommit))
            {
                var dir  = await GitFetcher.FetchAsync(depsRoot, name, gitUrl!, tagOrCommit!, ct);
                var hash = GitFetcher.ComputeDirectoryHash(dir);

                var depMap = await source.GetDependenciesAsync(name, version, ct);
                var entry = new LockEntry
                {
                    Version  = version.ToString(),
                    Resolved = $"git+{gitUrl}#{tagOrCommit}",
                    Integrity = hash,
                    Dependencies = depMap
                        .Where(kv => solved.ContainsKey(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
                };
                newLock.Packages[lockKey] = entry;
            }
            else if (source.IsRegistryPackage(name))
            {
                var registryUrl = source.GetRegistryUrl(name);
                var versionManifest = await source.GetVersionManifestAsync(name, version, ct);
                var tarballUrl = versionManifest?.Dist?.Tarball
                    ?? BuildTarballUrl(registryUrl, name, version.ToString());

                await PackageInstaller.InstallFromTarballAsync(
                    depsRoot, name, version.ToString(), tarballUrl,
                    config.GetRegistry(registryUrl).Token, ct);

                var depMap = await source.GetDependenciesAsync(name, version, ct);
                var entry = new LockEntry
                {
                    Version   = version.ToString(),
                    Resolved  = tarballUrl,
                    Integrity = versionManifest?.Dist?.Integrity,
                    Registry  = registryUrl,
                    Dependencies = depMap
                        .Where(kv => solved.ContainsKey(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
                };
                newLock.Packages[lockKey] = entry;
            }
            else
            {
                newLock.Packages[lockKey] = new LockEntry
                {
                    Version  = version.ToString(),
                    Resolved = $"unknown:{name}@{version}",
                };
            }
        }

        return newLock;
    }

    // ─── Root dependency collection ──────────────────────────────────────────

    /// <summary>Collect dependencies from package.json manifest.</summary>
    internal static Dictionary<string, VersionConstraint> CollectRootDependencies(
        PackageJsonManifest manifest)
    {
        var result = new Dictionary<string, VersionConstraint>(StringComparer.OrdinalIgnoreCase);

        // Regular registry deps
        if (manifest.Dependencies is not null)
        {
            foreach (var (name, constraint) in manifest.Dependencies)
                MergeConstraint(result, name, VersionConstraint.Parse(constraint));
        }

        // dotoriDependencies: git/path handled separately; version overrides merged
        if (manifest.DotoriDependencies is not null)
        {
            foreach (var (name, entry) in manifest.DotoriDependencies)
            {
                if (entry.Path is not null) continue; // path deps → build graph

                VersionConstraint c;
                if (entry.Git is not null)
                {
                    var tagOrCommit = entry.Tag ?? entry.Commit ?? "HEAD";
                    var vstr = tagOrCommit.StartsWith('v')
                        ? tagOrCommit[1..]
                        : tagOrCommit[..Math.Min(8, tagOrCommit.Length)];
                    c = SemanticVersion.TryParse(vstr, out _)
                        ? VersionConstraint.Parse(vstr)
                        : VersionConstraint.Any;
                }
                else if (entry.Version is not null)
                    c = VersionConstraint.Parse(entry.Version);
                else
                    c = VersionConstraint.Any;

                MergeConstraint(result, name, c);
            }
        }

        return result;
    }

    /// <summary>Collect dependencies from .dotori DSL (legacy).</summary>
    internal static Dictionary<string, VersionConstraint> CollectRootDependencies(
        ProjectDecl project)
    {
        var result = new Dictionary<string, VersionConstraint>(StringComparer.OrdinalIgnoreCase);

        var deps = project.Items
            .OfType<DependenciesBlock>()
            .SelectMany(b => b.Items);

        foreach (var dep in deps)
        {
            if (dep.Value is ComplexDependency { Path: not null }) continue;

            VersionConstraint c;
            if (dep.Value is VersionDependency ver)
                c = VersionConstraint.Parse(ver.Version);
            else if (dep.Value is ComplexDependency git && git.Git is not null)
            {
                var tc = git.Tag ?? git.Commit ?? "HEAD";
                var vstr = tc.StartsWith('v') ? tc[1..] : tc[..Math.Min(8, tc.Length)];
                c = SemanticVersion.TryParse(vstr, out _) ? VersionConstraint.Parse(vstr) : VersionConstraint.Any;
            }
            else if (dep.Value is ComplexDependency cv && cv.Version is not null)
                c = VersionConstraint.Parse(cv.Version);
            else
                c = VersionConstraint.Any;

            MergeConstraint(result, dep.Name, c);
        }

        return result;
    }

    private static void MergeConstraint(
        Dictionary<string, VersionConstraint> result,
        string name, VersionConstraint c)
    {
        if (result.TryGetValue(name, out var existing))
        {
            var merged = VersionConstraint.Intersect(existing, c)
                ?? throw new PackageManagerException(
                    $"Incompatible constraints for '{name}': {existing} and {c}");
            result[name] = merged;
        }
        else
        {
            result[name] = c;
        }
    }

    // ─── Git registration ────────────────────────────────────────────────────

    private static void RegisterGitDependencies(
        Dictionary<string, DotoriDependencyEntry> dotoriDeps,
        RegistryPackageSource source)
    {
        foreach (var (name, entry) in dotoriDeps)
        {
            if (entry.Git is not null)
                source.RegisterGit(name, entry.Git, entry.Tag ?? entry.Commit ?? "HEAD");
        }
    }

    private static void RegisterGitDependencies(ProjectDecl project, RegistryPackageSource source)
    {
        var deps = project.Items
            .OfType<DependenciesBlock>()
            .SelectMany(b => b.Items);

        foreach (var dep in deps)
        {
            if (dep.Value is ComplexDependency git && git.Git is not null)
                source.RegisterGit(dep.Name, git.Git, git.Tag ?? git.Commit ?? "HEAD");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string BuildTarballUrl(string registryUrl, string scopedName, string version)
    {
        // @owner/name → name (for filename)
        var slash = scopedName.LastIndexOf('/');
        var pkgName = slash >= 0 ? scopedName[(slash + 1)..] : scopedName;
        var encoded = Uri.EscapeDataString(scopedName);
        return $"{registryUrl.TrimEnd('/')}/{encoded}/-/{pkgName}-{version}.tgz";
    }
}
