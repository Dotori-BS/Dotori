using Dotori.Core.Location;
using Dotori.Core.Parsing;

namespace Dotori.Core.Graph;

/// <summary>
/// A node in the project dependency DAG.
/// </summary>
public sealed class ProjectNode
{
    public required string DotoriPath  { get; init; }
    public required string ProjectDir  { get; init; }
    public required string ProjectName { get; init; }

    /// <summary>Direct path-dependency nodes (already resolved).</summary>
    public List<ProjectNode> Dependencies { get; } = new();
}

/// <summary>
/// Builds a DAG of projects connected by <c>path =</c> dependencies,
/// detects cycles, and computes a topological build order with parallel levels.
/// </summary>
public static class ProjectDagBuilder
{
    /// <summary>
    /// Load the project graph starting from one or more root .dotori files.
    /// All reachable path-dependency projects are recursively loaded.
    /// </summary>
    /// <param name="rootPaths">Absolute paths to the root .dotori files.</param>
    /// <param name="gitPackageMap">Optional mapping of git package name → local .dotori path.</param>
    /// <returns>Map from dotoriPath → <see cref="ProjectNode"/>.</returns>
    /// <exception cref="CircularDependencyException">If a cycle is detected.</exception>
    public static IReadOnlyDictionary<string, ProjectNode> Build(
        IEnumerable<string> rootPaths,
        IReadOnlyDictionary<string, string>? gitPackageMap = null)
    {
        var nodes = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in rootPaths)
            LoadRecursive(root, nodes, gitPackageMap);

        CheckCycles(nodes);
        return nodes;
    }

    /// <summary>
    /// Return projects in topological order (dependencies before dependents).
    /// </summary>
    public static IReadOnlyList<ProjectNode> TopologicalSort(
        IReadOnlyDictionary<string, ProjectNode> nodes)
    {
        var result  = new List<ProjectNode>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes.Values)
            Visit(node, visited, result);

        return result;
    }

    /// <summary>
    /// Group nodes into parallel build levels.
    /// All nodes within one level have no dependency on each other
    /// and can be built concurrently once the previous level is complete.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ProjectNode>> BuildLevels(
        IReadOnlyDictionary<string, ProjectNode> nodes)
    {
        // Compute in-degrees (within the subgraph of nodes)
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Values)
        {
            inDegree.TryAdd(node.DotoriPath, 0);
            foreach (var dep in node.Dependencies)
                inDegree.TryAdd(dep.DotoriPath, 0);
        }
        // Each dependency must complete before its dependent can enter the queue.
        foreach (var node in nodes.Values)
            foreach (var dep in node.Dependencies)
                inDegree[node.DotoriPath]++;

        // Kahn's algorithm
        var queue = new Queue<ProjectNode>();
        foreach (var node in nodes.Values)
            if (inDegree[node.DotoriPath] == 0)
                queue.Enqueue(node);

        // Build reverse map: dep → list of nodes that depend on it
        var successors = new Dictionary<string, List<ProjectNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Values)
        {
            foreach (var dep in node.Dependencies)
            {
                if (!successors.TryGetValue(dep.DotoriPath, out var list))
                {
                    list = new List<ProjectNode>();
                    successors[dep.DotoriPath] = list;
                }
                list.Add(node);
            }
        }

        var levels = new List<IReadOnlyList<ProjectNode>>();
        while (queue.Count > 0)
        {
            var level = new List<ProjectNode>();
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                var node = queue.Dequeue();
                level.Add(node);

                if (successors.TryGetValue(node.DotoriPath, out var successorList))
                {
                    foreach (var successor in successorList)
                    {
                        inDegree[successor.DotoriPath]--;
                        if (inDegree[successor.DotoriPath] == 0)
                            queue.Enqueue(successor);
                    }
                }
            }
            levels.Add(level);
        }

        return levels;
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private static void LoadRecursive(
        string dotoriPath,
        Dictionary<string, ProjectNode> nodes,
        IReadOnlyDictionary<string, string>? gitPackageMap)
    {
        var normalized = Path.GetFullPath(dotoriPath);
        if (nodes.ContainsKey(normalized)) return;

        var file = DotoriParser.ParseFile(normalized);
        if (file.Project is null) return;

        var node = new ProjectNode
        {
            DotoriPath  = normalized,
            ProjectDir  = Path.GetDirectoryName(normalized)!,
            ProjectName = file.Project.Name,
        };
        // Register before recursing to handle (will detect cycle afterwards)
        nodes[normalized] = node;

        // path dependencies
        var pathDeps = ExtractPathDependencies(file.Project, normalized);
        foreach (var (_, depPath) in pathDeps)
        {
            LoadRecursive(depPath, nodes, gitPackageMap);
            if (nodes.TryGetValue(Path.GetFullPath(depPath), out var depNode))
                node.Dependencies.Add(depNode);
        }

        // git dependencies (only if local .dotori paths are known)
        if (gitPackageMap is not null)
        {
            foreach (var name in ExtractGitDependencyNames(file.Project))
            {
                if (!gitPackageMap.TryGetValue(name, out var gitDotoriPath)) continue;
                LoadRecursive(gitDotoriPath, nodes, gitPackageMap);
                if (nodes.TryGetValue(Path.GetFullPath(gitDotoriPath), out var gitNode))
                    node.Dependencies.Add(gitNode);
            }
        }
    }

    private static IEnumerable<(string Name, string DotoriPath)> ExtractPathDependencies(
        ProjectDecl project, string ownerDotoriPath)
    {
        var ownerDir = Path.GetDirectoryName(ownerDotoriPath)!;

        return project.Items
            .OfType<DependenciesBlock>()
            .SelectMany(b => b.Items)
            .Where(i => i.Value is ComplexDependency d && d.Path is not null)
            .Select(i =>
            {
                var rel  = ((ComplexDependency)i.Value).Path!;
                var dir  = Path.GetFullPath(Path.Combine(ownerDir, rel));
                var file = ProjectLocator.ResolveExplicitPath(dir);
                return (i.Name, file);
            });
    }

    private static IEnumerable<string> ExtractGitDependencyNames(ProjectDecl project)
    {
        return project.Items
            .OfType<DependenciesBlock>()
            .SelectMany(b => b.Items)
            .Where(i => (i.Value is ComplexDependency d && d.Git is not null) ||
                         i.Value is VersionDependency)
            .Select(i => i.Name);
    }

    private static void CheckCycles(Dictionary<string, ProjectNode> nodes)
    {
        // Build adjacency: node → its dependency nodes
        var adjacency = nodes.Values.ToDictionary(
            n => n.DotoriPath,
            n => (IReadOnlyList<string>)n.Dependencies.Select(d => d.DotoriPath).ToList(),
            StringComparer.OrdinalIgnoreCase);

        var cycle = CycleDetector.FindCycle(adjacency);
        if (cycle is not null)
        {
            var names = cycle.Select(p =>
                nodes.TryGetValue(p, out var n) ? n.ProjectName : p);
            throw new CircularDependencyException(
                "Circular dependency detected: " + string.Join(" → ", names));
        }
    }

    private static void Visit(
        ProjectNode node,
        HashSet<string> visited,
        List<ProjectNode> result)
    {
        if (!visited.Add(node.DotoriPath)) return;
        foreach (var dep in node.Dependencies)
            Visit(dep, visited, result);
        result.Add(node);
    }
}

/// <summary>Thrown when a circular dependency is detected in the project graph.</summary>
public sealed class CircularDependencyException(string message) : Exception(message);
