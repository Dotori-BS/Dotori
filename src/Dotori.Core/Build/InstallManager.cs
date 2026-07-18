using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Core.Build;

public sealed class InstallManifest
{
    public required string Project { get; init; }
    public required string Prefix { get; init; }
    public List<string> Files { get; init; } = [];
}

public sealed record InstallFile(string Source, string Destination);

[JsonSerializable(typeof(InstallManifest))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class InstallManifestJsonContext : JsonSerializerContext { }

/// <summary>Installs built artifacts and public headers using an auditable removal manifest.</summary>
public static class InstallManager
{
    public static IReadOnlyList<string> Install(
        FlatProjectModel model,
        string? artifactPath,
        string prefix,
        string? destDir,
        string targetId,
        string config)
    {
        var effectivePrefix = ResolveEffectivePrefix(prefix, destDir);
        var manifestPath = GetManifestPath(
            effectivePrefix, model.Name, targetId, config);
        var manifest = LoadManifest(manifestPath) ?? new InstallManifest
        {
            Project = model.Name,
            Prefix = Path.GetFullPath(effectivePrefix),
        };
        var sources = PlanInstall(model, artifactPath, prefix, destDir);

        foreach (var (source, destination) in sources)
        {
            CopyAtomic(source, destination);
            if (!manifest.Files.Contains(destination, PathComparer()))
                manifest.Files.Add(destination);
            SaveManifest(manifestPath, manifest);
        }
        var desired = sources.Select(pair => pair.Destination)
            .ToHashSet(PathComparer());
        foreach (var stale in manifest.Files.Where(file => !desired.Contains(file)).ToArray())
        {
            if (IsInside(stale, effectivePrefix) && File.Exists(stale)) File.Delete(stale);
            manifest.Files.Remove(stale);
        }
        SaveManifest(manifestPath, manifest);
        return sources.Select(pair => pair.Destination).ToArray();
    }

    public static IReadOnlyList<InstallFile> PlanInstall(
        FlatProjectModel model, string? artifactPath, string prefix, string? destDir)
    {
        var effectivePrefix = ResolveEffectivePrefix(prefix, destDir);
        return CollectFiles(model, artifactPath, effectivePrefix)
            .Select(pair => new InstallFile(pair.Source, pair.Destination))
            .ToArray();
    }

    public static IReadOnlyList<string> Uninstall(
        string projectName,
        string prefix,
        string? destDir,
        string targetId,
        string config)
    {
        var effectivePrefix = ResolveEffectivePrefix(prefix, destDir);
        var manifestPath = GetManifestPath(effectivePrefix, projectName, targetId, config);
        var files = PlanUninstall(projectName, prefix, destDir, targetId, config);
        var removed = new List<string>();
        foreach (var fullPath in files)
        {
            if (!File.Exists(fullPath)) continue;
            File.Delete(fullPath);
            removed.Add(fullPath);
            RemoveEmptyParents(Path.GetDirectoryName(fullPath), effectivePrefix);
        }
        File.Delete(manifestPath);
        RemoveEmptyParents(Path.GetDirectoryName(manifestPath), effectivePrefix);
        return removed;
    }

    public static IReadOnlyList<string> PlanUninstall(
        string projectName, string prefix, string? destDir, string targetId, string config)
    {
        var effectivePrefix = ResolveEffectivePrefix(prefix, destDir);
        var manifestPath = GetManifestPath(effectivePrefix, projectName, targetId, config);
        var manifest = LoadManifest(manifestPath)
            ?? throw new FileNotFoundException(
                $"No install manifest exists for '{projectName}' ({targetId}-{config}).",
                manifestPath);
        var files = manifest.Files.Select(Path.GetFullPath).ToArray();
        var invalid = files.FirstOrDefault(file => !IsInside(file, effectivePrefix));
        if (invalid is not null)
            throw new InvalidDataException(
                $"Install manifest contains a path outside its prefix: {invalid}");
        return files;
    }

    public static string ResolveEffectivePrefix(string prefix, string? destDir)
    {
        var fullPrefix = Path.GetFullPath(prefix);
        if (string.IsNullOrWhiteSpace(destDir)) return fullPrefix;
        var root = Path.GetPathRoot(fullPrefix) ?? Path.DirectorySeparatorChar.ToString();
        var relative = Path.GetRelativePath(root, fullPrefix);
        return Path.GetFullPath(Path.Combine(destDir, relative));
    }

    public static string GetManifestPath(
        string effectivePrefix, string projectName, string targetId, string config) =>
        Path.Combine(effectivePrefix, "share", "dotori", "install-manifests",
            $"{Sanitize(projectName)}-{Sanitize(targetId)}-{Sanitize(config)}.json");

    private static IReadOnlyList<(string Source, string Destination)> CollectFiles(
        FlatProjectModel model, string? artifactPath, string prefix)
    {
        if (model.Type == ProjectType.Test)
            throw new InvalidOperationException(
                $"Test project '{model.Name}' is not installable.");

        var result = new Dictionary<string, string>(PathComparer());
        if (model.Type != ProjectType.HeaderOnly)
        {
            if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
                throw new FileNotFoundException(
                    $"Built artifact for '{model.Name}' was not found. Run 'dotori build' first.",
                    artifactPath);
            var subdirectory = model.Type == ProjectType.Executable ? "bin" : "lib";
            result[Path.Combine(prefix, subdirectory, Path.GetFileName(artifactPath))] =
                Path.GetFullPath(artifactPath);
        }

        foreach (var header in model.Headers.Where(header => header.IsPublic))
        {
            var source = PathUtils.MakeAbsolute(model.ProjectDir, header.Path);
            if (File.Exists(source))
            {
                Add(result, source, Path.Combine(prefix, "include", Path.GetFileName(source)));
                continue;
            }
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"Public header path does not exist: {source}");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                Add(result, file, Path.Combine(prefix, "include", Path.GetRelativePath(source, file)));
        }
        return result.Select(pair => (pair.Value, pair.Key)).ToArray();
    }

    private static void Add(Dictionary<string, string> files, string source, string destination)
    {
        if (files.TryGetValue(destination, out var existing) &&
            !existing.Equals(source, PathComparison()))
            throw new InvalidDataException($"Multiple install sources map to: {destination}");
        files[destination] = Path.GetFullPath(source);
    }

    private static void CopyAtomic(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = $"{destination}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static InstallManifest? LoadManifest(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize(
            File.ReadAllText(path, Encoding.UTF8),
            InstallManifestJsonContext.Default.InstallManifest);
    }

    private static void SaveManifest(string path, InstallManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        manifest.Files.Sort(PathComparer());
        var json = JsonSerializer.Serialize(
            manifest, InstallManifestJsonContext.Default.InstallManifest);
        var temporary = $"{path}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, json, Encoding.UTF8);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RemoveEmptyParents(string? directory, string stopAt)
    {
        var stop = Path.GetFullPath(stopAt);
        while (directory is not null &&
               !Path.GetFullPath(directory).Equals(stop, PathComparison()) &&
               Directory.Exists(directory) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsInside(string path, string rootPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, PathComparison());
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
