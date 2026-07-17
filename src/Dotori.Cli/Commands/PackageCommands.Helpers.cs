using Dotori.Core.Parsing;
using Dotori.PackageManager;

namespace Dotori.Cli.Commands;

internal static partial class PackageCommandFactory
{
    // ─── Dependency collection ─────────────────────────────────────────────────

    /// <summary>
    /// Collect all dependency items from the project, including those inside condition blocks.
    /// De-duplicates by name (last declaration wins for display purposes).
    /// </summary>
    private static List<DependencyItem> CollectAllDeps(ProjectDecl project)
    {
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var map   = new Dictionary<string, DependencyItem>(StringComparer.OrdinalIgnoreCase);

        CollectDepsFromItems(project.Items, seen, order, map);
        return order.Select(n => map[n]).ToList();
    }

    private static void CollectDepsFromItems(
        IEnumerable<ProjectItem> items,
        HashSet<string> seen,
        List<string> order,
        Dictionary<string, DependencyItem> map)
    {
        foreach (var item in items)
        {
            if (item is DependenciesBlock db)
            {
                foreach (var dep in db.Items)
                {
                    if (seen.Add(dep.Name))
                        order.Add(dep.Name);
                    map[dep.Name] = dep;
                }
            }
            else if (item is ConditionBlock cb)
            {
                CollectDepsFromItems(cb.Items, seen, order, map);
            }
        }
    }

    // ─── DSL text manipulation ─────────────────────────────────────────────────

    /// <summary>
    /// Insert a dependency line into the first `dependencies { }` block in the file text.
    /// If no dependencies block exists, appends one before the project's closing brace.
    /// Returns the modified text, or null on failure.
    /// </summary>
    private static string? InsertDependency(string text, string depLine)
    {
        var lines = text.Split('\n').ToList();

        // Look for an existing `dependencies {` line
        int depsStart = -1;
        int depsBrace = 0;
        int insertBefore = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (depsStart < 0)
            {
                if (trimmed.StartsWith("dependencies") && trimmed.Contains('{'))
                {
                    depsStart = i;
                    depsBrace = CountBraces(lines[i]);
                }
            }
            else
            {
                depsBrace += CountBraces(lines[i]);
                if (depsBrace <= 0)
                {
                    // Found the closing brace of dependencies block
                    insertBefore = i;
                    break;
                }
            }
        }

        if (insertBefore >= 0)
        {
            // Insert before the closing brace of the dependencies block
            lines.Insert(insertBefore, depLine);
            return string.Join('\n', lines);
        }

        // No dependencies block — find last `}` of the project block and insert a new block before it
        int lastBrace = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Trim() == "}")
            {
                lastBrace = i;
                break;
            }
        }

        if (lastBrace < 0) return null;

        // Insert a new dependencies block before the last `}`
        lines.Insert(lastBrace, "");
        lines.Insert(lastBrace + 1, "    dependencies {");
        lines.Insert(lastBrace + 2, depLine);
        lines.Insert(lastBrace + 3, "    }");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Remove a dependency by name from the file text.
    /// Returns modified text, or null if the dependency was not found.
    /// </summary>
    private static string? RemoveDependency(string text, string name)
    {
        var lines    = text.Split('\n').ToList();
        var prefix   = name + " ";
        var prefixEq = name + "=";

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            // Match lines like `name = ...` inside a dependencies block
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(prefixEq, StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                return string.Join('\n', lines);
            }
        }

        return null;
    }

    private static int CountBraces(string line)
    {
        int count = 0;
        bool inString = false;
        foreach (var ch in line)
        {
            if (ch == '"') inString = !inString;
            if (!inString)
            {
                if (ch == '{') count++;
                else if (ch == '}') count--;
            }
        }
        return count;
    }

    private static string ExtractGitName(string url)
    {
        // e.g. https://github.com/fmtlib/fmt or https://github.com/fmtlib/fmt.git → fmt
        var u = url.TrimEnd('/');
        if (u.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            u = u[..^4];
        var slash = u.LastIndexOf('/');
        return slash >= 0 ? u[(slash + 1)..] : u;
    }

    // ─── Lock file ────────────────────────────────────────────────────────────

    /// <summary>Re-resolve the lock file for the given project.</summary>
    private static async Task<int> UpdateLockAsync(
        string dotoriPath, string projectDir, string? specificName, CancellationToken ct)
    {
        try
        {
            var file = DotoriParser.ParseFile(dotoriPath);
            if (file.Project is null)
            {
                Console.Error.WriteLine($"Error: No project declaration in '{dotoriPath}'.");
                return 1;
            }

            var existingLock = LockManager.Load(projectDir);

            // If updating a specific package, remove it from the lock to force re-fetch
            if (specificName is not null)
            {
                var toRemove = existingLock.Packages.Keys
                    .Where(k =>
                    {
                        var at = k.LastIndexOf('@');
                        var name = at > 0 ? k[..at] : k;
                        return name.Equals(specificName, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
                foreach (var k in toRemove)
                    existingLock.Packages.Remove(k);
            }
            else
            {
                // Full update: start fresh
                existingLock = new LockFile();
            }

            var newLock = await DependencyResolver.ResolveAsync(
                projectDir, existingLock, file.Project, ct: ct);
            LockManager.Save(newLock, projectDir);

            Console.WriteLine($"  Lock file updated ({newLock.Packages.Count} package(s)).");
            return 0;
        }
        catch (PackageManagerException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error updating lock file: {ex.Message}");
            return 1;
        }
    }
}
