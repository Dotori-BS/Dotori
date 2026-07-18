using Dotori.Core;
using Dotori.Core.Parsing;

namespace Dotori.Core.Location;

/// <summary>
/// Result of a project search operation.
/// </summary>
public sealed class LocatedProject
{
    public required string DotoriPath  { get; init; }
    public required string ProjectDir  { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectType { get; init; }
}

/// <summary>
/// Locates .dotori files according to the search order defined in SPEC.md §3:
/// 1. --project option path
/// 2. Current directory
/// 3. Parent directories up to git root
/// 4. Subdirectories search
/// 5. Auto-select single / interactive prompt for multiple / error for none
/// </summary>
public static class ProjectLocator
{
    private const string DotoriFileName = ".dotori";

    /// <summary>
    /// Resolve a --project argument (file path or directory).
    /// Returns the absolute path to the .dotori file, or throws if not found.
    /// </summary>
    public static string ResolveExplicitPath(string projectArg)
    {
        var full = Path.GetFullPath(projectArg);

        if (File.Exists(full))
        {
            if (Path.GetFileName(full) == DotoriFileName || full.EndsWith(DotoriFileName))
                return full;
            throw new ProjectLocatorException($"File '{full}' is not a .dotori file.");
        }

        if (Directory.Exists(full))
        {
            var candidate = Path.Combine(full, DotoriFileName);
            if (File.Exists(candidate))
                return candidate;
            throw new ProjectLocatorException($"No .dotori file found in '{full}'.");
        }

        throw new ProjectLocatorException($"Path '{full}' does not exist.");
    }

    /// <summary>
    /// Search for .dotori files starting from <paramref name="startDir"/>.
    /// Returns all found .dotori paths according to the spec search order.
    /// </summary>
    public static IReadOnlyList<string> FindDotoriFiles(string startDir)
    {
        // Step 2: current directory
        var direct = Path.Combine(startDir, DotoriFileName);
        if (File.Exists(direct))
            return [direct];

        // Step 3: walk up to git root (inclusive)
        var ancestor = FindInAncestors(startDir);
        if (ancestor is not null)
            return [ancestor];

        // Step 4: walk down into subdirectories
        var found = FindInDescendants(startDir);
        return found;
    }

    /// <summary>
    /// Recursively finds every project below a directory, including a project at
    /// the directory root. Unlike normal discovery, finding a project does not
    /// stop traversal into nested workspace projects.
    /// </summary>
    public static IReadOnlyList<string> FindAllDotoriFiles(string rootDir)
    {
        var results = new List<string>();
        CollectDotoriFiles(Path.GetFullPath(rootDir), results, depth: 0, maxDepth: 8,
            stopAtProjectRoot: false);
        return results;
    }

    /// <summary>
    /// Walk up parent directories until a .dotori is found or the git root is passed.
    /// Returns the path if found, null otherwise.
    /// </summary>
    private static string? FindInAncestors(string startDir)
    {
        var gitRoot = FindGitRoot(startDir);
        var current = Directory.GetParent(startDir)?.FullName;

        while (current is not null)
        {
            var candidate = Path.Combine(current, DotoriFileName);
            if (File.Exists(candidate))
                return candidate;

            // Stop if we've reached the git root (already checked it)
            if (gitRoot is not null &&
                string.Equals(current, gitRoot, StringComparison.OrdinalIgnoreCase))
                break;

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Recursively collect all .dotori files in subdirectories.
    /// Skips hidden directories and common build/cache directories.
    /// </summary>
    private static IReadOnlyList<string> FindInDescendants(string rootDir)
    {
        var results = new List<string>();
        CollectDotoriFiles(rootDir, results, depth: 0, maxDepth: 8,
            stopAtProjectRoot: true);
        return results;
    }

    private static void CollectDotoriFiles(
        string dir,
        List<string> results,
        int depth,
        int maxDepth,
        bool stopAtProjectRoot)
    {
        if (depth > maxDepth) return;

        var candidate = Path.Combine(dir, DotoriFileName);
        if (File.Exists(candidate))
        {
            results.Add(candidate);
            if (stopAtProjectRoot) return;
        }

        try
        {
            foreach (var subDir in Directory.GetDirectories(dir)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(subDir);
                if (ShouldSkipDirectory(name)) continue;
                CollectDotoriFiles(subDir, results, depth + 1, maxDepth, stopAtProjectRoot);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static bool ShouldSkipDirectory(string name) =>
        name.StartsWith('.') ||
        name is "bin" or "obj" or "node_modules" or
                "build" or "out" or DotoriConstants.CacheDir;

    /// <summary>
    /// Find the git repository root by walking up looking for a .git directory.
    /// Returns null if not in a git repository.
    /// </summary>
    public static string? FindGitRoot(string startDir)
    {
        var current = startDir;
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// Parse a .dotori file and return a <see cref="LocatedProject"/> summary.
    /// </summary>
    public static LocatedProject LoadProject(string dotoriPath)
    {
        var file = DotoriParser.ParseFile(dotoriPath);
        var name = file.Project?.Name ?? Path.GetFileName(Path.GetDirectoryName(dotoriPath) ?? dotoriPath);
        var type = file.Project?.Items
            .OfType<ProjectTypeProp>()
            .FirstOrDefault()?.Value.ToString().ToLower() ?? "unknown";

        return new LocatedProject
        {
            DotoriPath  = dotoriPath,
            ProjectDir  = Path.GetDirectoryName(dotoriPath)!,
            ProjectName = name,
            ProjectType = type,
        };
    }

    /// <summary>
    /// Interactive project selection prompt. Returns selected paths.
    /// </summary>
    /// <param name="projects">List of located projects to choose from.</param>
    /// <param name="buildAll">If true, skip prompt and return all projects.</param>
    /// <param name="stdin">Reader for user input (defaults to Console.In).</param>
    /// <param name="stdout">Writer for prompt output (defaults to Console.Error).</param>
    /// <returns>Selected project paths, or all if "all" is chosen.</returns>
    public static IReadOnlyList<string> PromptSelection(
        IReadOnlyList<LocatedProject> projects,
        bool buildAll = false,
        TextReader? stdin  = null,
        TextWriter? stdout = null)
    {
        stdin  ??= Console.In;
        stdout ??= Console.Error;

        if (buildAll || projects.Count == 1)
            return projects.Select(p => p.DotoriPath).ToList();

        stdout.WriteLine("No .dotori file found in current directory.");
        stdout.WriteLine($"Found {projects.Count} projects:");
        stdout.WriteLine();

        for (int i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            stdout.WriteLine($"  [{i + 1}] {p.ProjectName,-20} ({p.ProjectType,-16}) {p.DotoriPath}");
        }

        stdout.WriteLine();
        stdout.Write($"Select project [1-{projects.Count}] or press Enter to build all: ");

        var input = stdin.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return projects.Select(p => p.DotoriPath).ToList();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= projects.Count)
            return [projects[choice - 1].DotoriPath];

        throw new ProjectLocatorException(
            $"Invalid selection '{input}'. Enter a number between 1 and {projects.Count}, or press Enter for all.");
    }
}

/// <summary>Exception thrown when project location fails.</summary>
public sealed class ProjectLocatorException(string message) : Exception(message);
