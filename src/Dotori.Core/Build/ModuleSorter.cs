using Dotori.Core;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

/// <summary>
/// Topologically sorts module interface files based on their import/export dependencies.
/// BMIs (Binary Module Interface files) must be built in dependency order:
/// if module A imports module B, B's BMI must be built before A.
/// </summary>
public static class ModuleSorter
{
    /// <summary>
    /// Sort module dependency info into a build order.
    /// </summary>
    /// <param name="deps">All scanned module dependencies.</param>
    /// <returns>
    /// Ordered list of source files — BMIs for files earlier in the list
    /// must be produced before files later in the list.
    /// Files with no module dependencies come first.
    /// Throws <see cref="InvalidOperationException"/> if a cycle is detected.
    /// </returns>
    public static IReadOnlyList<ModuleScanner.ModuleDep> Sort(
        IReadOnlyList<ModuleScanner.ModuleDep> deps)
    {
        // Build a map: logical module name → ModuleDep
        var byName = new Dictionary<string, ModuleScanner.ModuleDep>(StringComparer.Ordinal);
        foreach (var dep in deps)
        {
            if (dep.Provides is not null)
                byName[dep.Provides] = dep;
        }

        // Kahn's algorithm (BFS topological sort)
        // Node = ModuleDep; Edge = dep.Provides → each dep.Requires
        // We want to order so that a file providing X comes BEFORE files requiring X.

        // in-degree: how many modules must be built before this one
        var inDegree  = new Dictionary<string, int>(StringComparer.Ordinal);
        // adjacency: provides → list of deps that require it
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var dep in deps)
        {
            var name = dep.Provides ?? dep.SourceFile;
            if (!inDegree.ContainsKey(name)) inDegree[name] = 0;
            if (!adjacency.ContainsKey(name)) adjacency[name] = new List<string>();
        }

        foreach (var dep in deps)
        {
            var depName = dep.Provides ?? dep.SourceFile;

            foreach (var req in dep.Requires)
            {
                // Only consider requires that are known (project-local modules)
                if (!byName.ContainsKey(req)) continue;

                // req must be built before depName
                inDegree[depName] = inDegree.GetValueOrDefault(depName) + 1;

                if (!adjacency.TryGetValue(req, out var list))
                    adjacency[req] = list = new List<string>();
                list.Add(depName);
            }
        }

        // BFS queue: start with all nodes with in-degree 0
        var queue = new Queue<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

        var result = new List<ModuleScanner.ModuleDep>();

        // Map name → dep for output reconstruction
        var depByName = new Dictionary<string, ModuleScanner.ModuleDep>(StringComparer.Ordinal);
        foreach (var dep in deps)
            depByName[dep.Provides ?? dep.SourceFile] = dep;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (depByName.TryGetValue(current, out var currentDep))
                result.Add(currentDep);

            if (adjacency.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }
        }

        // Cycle detection: if we didn't process all nodes, there's a cycle
        if (result.Count != deps.Count)
        {
            var remaining = inDegree
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .ToList();

            throw new InvalidOperationException(
                "Circular module dependency detected among: " +
                string.Join(", ", remaining));
        }

        return result;
    }

    /// <summary>
    /// Build compile jobs for module interface files in sorted order.
    /// Each file must be compiled with references to previously-built BMIs.
    /// </summary>
    /// <param name="sortedDeps">Already sorted module deps (from <see cref="Sort"/>).</param>
    /// <param name="objDir">Directory for output BMI files.</param>
    /// <param name="compilerKind">Compiler kind to determine BMI extension (.ifc vs .pcm).</param>
    /// <param name="commonFlags">Common compiler flags (without -c / /c).</param>
    /// <returns>Ordered list of compile jobs for BMI generation.</returns>
    public static IReadOnlyList<CompileJob> BuildModuleJobs(
        IReadOnlyList<ModuleScanner.ModuleDep> sortedDeps,
        string objDir,
        CompilerKind compilerKind,
        IReadOnlyList<string> commonFlags)
    {
        // Map: logical module name → generated BMI path
        var bmiPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var jobs     = new List<CompileJob>();

        var bmiDir = Path.Combine(objDir, DotoriConstants.BmiSubDir);
        Directory.CreateDirectory(bmiDir);

        foreach (var dep in sortedDeps)
        {
            var ext    = compilerKind == CompilerKind.Msvc ? ".ifc" : ".pcm";
            var bmiFile = dep.Provides is not null
                ? Path.Combine(bmiDir,
                    dep.Provides.Replace(':', '_').Replace('.', '_') + ext)
                : BuildOutputPath.ForSource(dep.SourceFile, bmiDir, ext);

            var args = new List<string>(commonFlags);

            if (compilerKind == CompilerKind.Msvc)
            {
                // MSVC: /interface /TP + references
                args.Remove("/c");
                args.Add("/interface");
                args.Add("/TP");

                foreach (var req in dep.Requires)
                {
                    if (bmiPaths.TryGetValue(req, out var reqBmi))
                        args.Add($"/reference {req}=\"{reqBmi}\"");
                }

                args.Add($"\"{dep.SourceFile}\"");
                args.Add($"/ifcOutput \"{bmiFile}\"");
            }
            else
            {
                // Clang: --precompile -x c++-module + -fmodule-file=
                args.Remove("-c");
                args.Add("--precompile");
                args.Add("-x c++-module");

                foreach (var req in dep.Requires)
                {
                    if (bmiPaths.TryGetValue(req, out var reqBmi))
                        args.Add($"-fmodule-file={req}=\"{reqBmi}\"");
                }

                args.Add($"\"{dep.SourceFile}\"");
                args.Add($"-o \"{bmiFile}\"");
            }

            jobs.Add(new CompileJob
            {
                SourceFile = dep.SourceFile,
                OutputFile = bmiFile,
                Args       = args.ToArray(),
            });

            if (dep.Provides is not null)
                bmiPaths[dep.Provides] = bmiFile;
        }

        return jobs;
    }

    /// <summary>
    /// Build the import flags for a regular .cpp file that imports modules.
    /// Adds /reference or -fmodule-file flags for each imported module.
    /// </summary>
    public static IReadOnlyList<string> BuildImportFlags(
        IEnumerable<string>               imports,
        IReadOnlyDictionary<string, string> bmiPaths,
        CompilerKind                       compilerKind)
    {
        var flags = new List<string>();

        foreach (var import in imports)
        {
            if (!bmiPaths.TryGetValue(import, out var bmiFile)) continue;

            if (compilerKind == CompilerKind.Msvc)
                flags.Add($"/reference {import}=\"{bmiFile}\"");
            else
                flags.Add($"-fmodule-file={import}=\"{bmiFile}\"");
        }

        return flags;
    }
}
