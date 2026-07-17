namespace Dotori.Core.Build;

/// <summary>
/// Scans C/C++ source files for #include directives and builds an include tree.
/// This is a compiler-independent, text-based scanner.
///
/// Limitations:
/// - Does not evaluate preprocessor conditionals (#ifdef, #if, etc.)
///   so all #include directives are treated as active.
/// - Does not handle multi-line macros or trigraphs.
/// </summary>
public static class IncludeScanner
{
    /// <summary>
    /// A single node in the include tree.
    /// </summary>
    public sealed class IncludeNode
    {
        /// <summary>Absolute path if resolved, or the raw include path if not.</summary>
        public required string FilePath { get; init; }

        /// <summary>True if this is a system include (&lt;...&gt;), false for local (&quot;...&quot;).</summary>
        public bool IsSystem { get; init; }

        /// <summary>True if the file was found on disk and children were scanned.</summary>
        public bool IsResolved { get; init; }

        /// <summary>Children are empty for unresolved or system-skipped nodes.</summary>
        public IReadOnlyList<IncludeNode> Children { get; init; } = Array.Empty<IncludeNode>();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parse all #include directives in a single source file.
    /// Returns a flat list of (path, isSystem) tuples in source order.
    /// </summary>
    public static IReadOnlyList<(string Path, bool IsSystem)> ParseIncludes(string sourceFile)
    {
        var result = new List<(string, bool)>();
        try
        {
            foreach (var line in File.ReadLines(sourceFile))
            {
                var (path, isSystem) = ExtractInclude(line);
                if (path is not null)
                    result.Add((path, isSystem));
            }
        }
        catch (IOException) { }
        return result;
    }

    /// <summary>
    /// Recursively build an include tree starting from <paramref name="rootFile"/>.
    /// </summary>
    /// <param name="rootFile">Absolute path to the root source file.</param>
    /// <param name="searchPaths">
    ///   Additional include search directories (from the project's headers block).
    ///   Used to resolve both local and system includes.
    /// </param>
    /// <param name="includeSystem">
    ///   When false, system includes (&lt;...&gt;) are listed as leaf nodes without recursion.
    /// </param>
    /// <param name="maxDepth">Maximum recursion depth (0 = root only, no children).</param>
    public static IncludeNode BuildTree(
        string rootFile,
        IReadOnlyList<string> searchPaths,
        bool includeSystem = true,
        int  maxDepth      = int.MaxValue)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return BuildNode(rootFile, isSystem: false, searchPaths, includeSystem, maxDepth, depth: 0, visited);
    }

    /// <summary>Return the resolved transitive includes of a source file.</summary>
    public static IReadOnlyList<string> CollectResolvedFiles(
        string rootFile, IReadOnlyList<string> searchPaths, bool includeSystem = false)
    {
        var tree = BuildTree(rootFile, searchPaths, includeSystem);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void Collect(IncludeNode node, string root, HashSet<string> output)
        {
            if (node.IsResolved && !string.Equals(node.FilePath, root,
                    StringComparison.OrdinalIgnoreCase))
                output.Add(node.FilePath);
            foreach (var child in node.Children)
                Collect(child, root, output);
        }

        Collect(tree, Path.GetFullPath(rootFile), files);
        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // ─── Tree construction ────────────────────────────────────────────────────

    private static IncludeNode BuildNode(
        string absolutePath,
        bool   isSystem,
        IReadOnlyList<string> searchPaths,
        bool   includeSystem,
        int    maxDepth,
        int    depth,
        HashSet<string> visited)
    {
        // Mark as visited before recursing to prevent cycles
        if (!visited.Add(absolutePath))
        {
            // Already processed — leaf node only
            return new IncludeNode
            {
                FilePath   = absolutePath,
                IsSystem   = isSystem,
                IsResolved = true,
                Children   = Array.Empty<IncludeNode>(),
            };
        }

        // Limit depth
        if (depth >= maxDepth || !File.Exists(absolutePath))
        {
            return new IncludeNode
            {
                FilePath   = absolutePath,
                IsSystem   = isSystem,
                IsResolved = File.Exists(absolutePath),
                Children   = Array.Empty<IncludeNode>(),
            };
        }

        var rawIncludes = ParseIncludes(absolutePath);
        var sourceDir   = Path.GetDirectoryName(absolutePath) ?? string.Empty;
        var children    = new List<IncludeNode>(rawIncludes.Count);

        foreach (var (rawPath, childIsSystem) in rawIncludes)
        {
            var resolved = ResolveHeader(rawPath, childIsSystem, sourceDir, searchPaths);

            if (resolved is null)
            {
                // Unresolved — leaf node
                children.Add(new IncludeNode
                {
                    FilePath   = rawPath,
                    IsSystem   = childIsSystem,
                    IsResolved = false,
                });
                continue;
            }

            if (childIsSystem && !includeSystem)
            {
                // System header excluded by user option — still list but no children
                children.Add(new IncludeNode
                {
                    FilePath   = resolved,
                    IsSystem   = true,
                    IsResolved = true,
                    Children   = Array.Empty<IncludeNode>(),
                });
                continue;
            }

            children.Add(BuildNode(resolved, childIsSystem, searchPaths, includeSystem, maxDepth, depth + 1, visited));
        }

        return new IncludeNode
        {
            FilePath   = absolutePath,
            IsSystem   = isSystem,
            IsResolved = true,
            Children   = children,
        };
    }

    // ─── Header resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Try to resolve a raw include path to an absolute file path.
    /// Returns null if the file cannot be found.
    /// </summary>
    public static string? ResolveHeader(
        string rawPath,
        bool   isSystem,
        string sourceDir,
        IReadOnlyList<string> searchPaths)
    {
        if (!isSystem)
        {
            // Local includes: try the directory of the including file first
            var candidate = Path.GetFullPath(Path.Combine(sourceDir, rawPath));
            if (File.Exists(candidate)) return candidate;
        }

        // Then try all search paths
        foreach (var dir in searchPaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, rawPath));
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    // ─── #include parsing ─────────────────────────────────────────────────────

    /// <summary>
    /// Extract the include path from a single source line.
    /// Returns (null, false) if the line is not a #include directive.
    /// </summary>
    internal static (string? Path, bool IsSystem) ExtractInclude(ReadOnlySpan<char> line)
    {
        // Skip leading whitespace
        line = line.TrimStart();

        // Must start with '#'
        if (line.IsEmpty || line[0] != '#') return (null, false);
        line = line[1..].TrimStart();

        // Must be "include"
        if (!line.StartsWith("include", StringComparison.Ordinal)) return (null, false);
        line = line["include".Length..].TrimStart();

        if (line.IsEmpty) return (null, false);

        char open = line[0];
        char close;
        bool isSystem;

        if (open == '"')        { close = '"';  isSystem = false; }
        else if (open == '<')   { close = '>';  isSystem = true;  }
        else                    return (null, false);

        line = line[1..];
        var end = line.IndexOf(close);
        if (end <= 0) return (null, false);

        var path = line[..end].ToString();
        return string.IsNullOrWhiteSpace(path) ? (null, false) : (path, isSystem);
    }
}
