using System.Collections.Concurrent;
using Dotori.Core;
using Dotori.Core.Build;
using Dotori.Core.Executor;
using Dotori.Core.Graph;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Cli.Commands;

internal sealed record ProjectBuildResult(
    int ExitCode,
    IReadOnlyDictionary<string, string> Outputs);

internal sealed record RunnableBuildResult(int ExitCode, IReadOnlyList<string> ExecutablePaths);

/// <summary>
/// Executes a project DAG through the single build pipeline used by build, run, and test.
/// Command-specific behavior such as launching the resulting executable stays outside this type.
/// </summary>
internal static class ProjectBuildEngine
{
    public static async Task<RunnableBuildResult> BuildRunnablesAsync(
        IReadOnlyList<string> projectPaths,
        string targetId,
        string config,
        ProjectType projectType,
        bool fallbackToExecutable = false,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, string> gitPackageMap;
        try
        {
            gitPackageMap = await BuildContext.ResolveAndFetchGitPackagesAsync(
                projectPaths, locked: false, offline: false, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: dependency resolution failed: {ex.Message}");
            return new RunnableBuildResult(1, []);
        }

        var dag = BuildContext.BuildDag(projectPaths, gitPackageMap);
        if (dag is null)
            return new RunnableBuildResult(1, []);

        var buildOrder = ProjectDagBuilder.BuildLevels(dag);
        var toolchain = BuildContext.DetectToolchain(
            targetId, cliCompiler: null, projectPaths.FirstOrDefault());
        if (toolchain is null)
            return new RunnableBuildResult(1, []);

        var declaredOptions = BuildContext.ScanOptions(dag.Values.Select(node => node.DotoriPath));
        var enabledOptions = BuildContext.ResolveEnabledOptions(
            declaredOptions,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var context = BuildContext.MakeTargetContext(
            targetId, config, enabledOptions: enabledOptions,
            declaredOptions: declaredOptions);

        var executor = new LocalExecutor(0);
        var result = await BuildAsync(
            buildOrder, context, toolchain, executor, targetId, config, ct: ct);
        if (result.ExitCode != 0)
            return new RunnableBuildResult(result.ExitCode, []);

        var paths = FindOutputs(buildOrder, result.Outputs, context, projectType);
        if (paths.Count == 0 && fallbackToExecutable && projectType != ProjectType.Executable)
            paths = FindOutputs(buildOrder, result.Outputs, context, ProjectType.Executable);
        return new RunnableBuildResult(0, paths);
    }

    public static async Task<ProjectBuildResult> BuildAsync(
        IReadOnlyList<IReadOnlyList<ProjectNode>> buildOrder,
        TargetContext context,
        ToolchainInfo toolchain,
        IExecutor executor,
        string targetId,
        string config,
        string? fileArg = null,
        bool noLink = false,
        bool noUnity = false,
        bool dryRun = false,
        bool explain = false,
        int[]? fileFound = null,
        BuildTraceSession? trace = null,
        CancellationToken ct = default)
    {
        var outputs = new ConcurrentDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        fileFound ??= [0];

        foreach (var level in buildOrder)
        {
            var tasks = level.Select(BuildNodeAsync);

            var results = await Task.WhenAll(tasks);
            if (results.Any(code => code != 0))
                return new ProjectBuildResult(1, outputs);
        }

        return new ProjectBuildResult(0, outputs);

        async Task<int> BuildNodeAsync(ProjectNode node)
        {
            var projectTrace = trace?.BeginProject(node.ProjectName, node.DotoriPath);
            var code = await BuildProjectAsync(
                node, context, toolchain, executor, targetId, config,
                fileArg, noLink, noUnity, dryRun, explain, outputs, fileFound,
                projectTrace, ct);
            projectTrace?.Complete(code == 0);
            return code;
        }
    }

    public static IReadOnlyList<string> FindOutputs(
        IReadOnlyList<IReadOnlyList<ProjectNode>> buildOrder,
        IReadOnlyDictionary<string, string> outputs,
        TargetContext context,
        ProjectType projectType)
    {
        var matches = new List<string>();
        foreach (var node in buildOrder.SelectMany(level => level))
        {
            var model = BuildContext.FlattenProject(node.DotoriPath, context, node);
            if (model?.Type == projectType &&
                outputs.TryGetValue(node.DotoriPath, out var output))
                matches.Add(output);
        }

        return matches;
    }

    internal static async Task<int> BuildProjectAsync(
        ProjectNode node,
        Dotori.Core.Model.TargetContext ctx,
        Dotori.Core.Toolchain.ToolchainInfo toolchain,
        IExecutor executor,
        string targetId,
        string config,
        string? fileArg,
        bool noLink,
        bool noUnity,
        bool dryRun,
        bool explain,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> builtLibraries,
        int[] fileFound,
        BuildTraceProject? traceProject,
        CancellationToken ct)
    {
        var model = BuildContext.FlattenProject(node.DotoriPath, ctx, node);
        if (model is null) return 1;

        Console.WriteLine($"  Building {model.Name} ({node.DotoriPath})");

        using var checker = new IncrementalChecker(model.ProjectDir, $"{targetId}-{config}");
        var planner = new BuildPlanner(model, toolchain, config, targetId);

        // Collect transitive dependency library paths
        var depLibs = CollectDepLibs(node, builtLibraries);

        // Determine link output dir for env vars
        var linkOutDir = Path.Combine(model.ProjectDir, DotoriConstants.CacheDir,
            DotoriConstants.BinSubDir, $"{targetId}-{config.ToLower()}");

        var ruleCode = await BuildRuleRunner.RunAsync(
            model.BuildRules, model.ProjectDir, targetId, config, linkOutDir,
            checker, dryRun, explain, ct);
        if (ruleCode != 0) return ruleCode;

        // pre-build scripts
        if (model.PreBuildCommands.Count > 0 && !dryRun)
        {
            var preCode = await BuildContext.RunScriptsAsync(
                model.PreBuildCommands, model.ProjectDir,
                targetId, config, linkOutDir, ct);
            if (preCode != 0) return preCode;
        }

        // PCH — plan once, used by both --file and normal build paths
        var pchPlan = planner.PlanPch(checker);

        // Single-file mode
        if (fileArg is not null)
        {
            var absFile = Path.GetFullPath(fileArg);
            var ext = Path.GetExtension(absFile);
            bool isModuleFile = ext.Equals(".cppm", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".ixx",  StringComparison.OrdinalIgnoreCase);

            // Plan PCH first (rebuild if stale)
            if (pchPlan?.BuildJob is not null)
            {
                var pchResults = await executor.RunCompileJobsAsync(
                    toolchain.CompilerPath,
                    new[] { pchPlan.BuildJob }, ct);
                var pchCode = PrintResults(pchResults);
                if (pchCode != 0) return pchCode;
                if (!dryRun)
                {
                    checker.Record(pchPlan.BuildJob.SourceFile);
                    foreach (var dependency in pchPlan.BuildJob.DependencyFiles)
                        checker.Record(dependency);
                    checker.RecordFingerprint(
                        pchPlan.BuildJob.OutputFile, pchPlan.BuildJob.Fingerprint);
                }
            }

            var singleJob = planner.PlanSingleFileJob(absFile, noUnity);

            if (singleJob is null)
            {
                // File not in this project — register existing library output for transitive linking
                var existingLinkOut = planner.PlanLinkJob(Enumerable.Empty<string>());
                if (existingLinkOut is not null && File.Exists(existingLinkOut.OutputFile))
                    builtLibraries[node.DotoriPath] = existingLinkOut.OutputFile;
                return 0;
            }

            // Mark file as found in this project
            System.Threading.Interlocked.Exchange(ref fileFound[0], 1);
            traceProject?.RecordCompiles(1, 1);

            var fileResults = await executor.RunCompileJobsAsync(
                toolchain.CompilerPath, new[] { singleJob }, ct);
            int fileCode = PrintResults(fileResults);
            if (fileCode != 0) return fileCode;

            // Module files (.cppm/.ixx): --no-link is implicit
            if (isModuleFile || noLink)
                return 0;

            // Link with existing obj files + this new obj
            var objCacheDir = Path.Combine(model.ProjectDir, DotoriConstants.CacheDir,
                DotoriConstants.ObjSubDir, $"{targetId}-{config.ToLower()}");
            var existingObjs = Directory.Exists(objCacheDir)
                ? Directory.GetFiles(objCacheDir, "*.o")
                  .Concat(Directory.GetFiles(objCacheDir, "*.obj"))
                  .ToList()
                : new List<string>();

            // Replace any stale obj for this file with the new one, include dep libs
            var allObjs = existingObjs
                .Where(f => !string.Equals(f, singleJob.OutputFile,
                    StringComparison.OrdinalIgnoreCase))
                .Append(singleJob.OutputFile)
                .Concat(depLibs)
                .ToList();

            var singleLinkJob = planner.PlanLinkJob(allObjs);
            if (singleLinkJob is null) return 0;

            var singleLinkResult = await executor.RunLinkJobAsync(planner.GetLinkerPath(), singleLinkJob, ct);
            if (!singleLinkResult.Success)
            {
                Console.Error.WriteLine(singleLinkResult.Stderr);
                return 1;
            }
            Console.WriteLine($"  Linked: {singleLinkJob.OutputFile}");
            return 0;
        }

        // Normal build — PCH first (pchPlan already computed above)
        if (pchPlan?.BuildJob is not null)
        {
            var pchResults = await executor.RunCompileJobsAsync(
                toolchain.CompilerPath, new[] { pchPlan.BuildJob }, ct);
            int pchCode = PrintResults(pchResults);
            if (pchCode != 0) return pchCode;
            if (!dryRun)
            {
                checker.Record(pchPlan.BuildJob.SourceFile);
                foreach (var dependency in pchPlan.BuildJob.DependencyFiles)
                    checker.Record(dependency);
                checker.RecordFingerprint(
                    pchPlan.BuildJob.OutputFile, pchPlan.BuildJob.Fingerprint);
            }
        }

        // Modules next (order-sensitive BMI generation)
        var moduleJobs = await planner.PlanModuleJobsAsync(ct: ct);
        IReadOnlyDictionary<string, string>? bmiPaths = null;
        if (moduleJobs.Count > 0)
        {
            // BMI jobs must run in dependency order (sequential)
            foreach (var bmiJob in moduleJobs)
            {
                var bmiResults = await executor.RunCompileJobsAsync(
                    toolchain.CompilerPath, new[] { bmiJob }, ct);
                int bmiCode = PrintResults(bmiResults);
                if (bmiCode != 0) return bmiCode;
            }
            bmiPaths = BuildPlanner.ExtractBmiPaths(moduleJobs);
            // Write module-map.json after all BMIs are compiled
            if (!dryRun) planner.WriteModuleMap(moduleJobs);
        }

        var dependencyModuleDirectories = node.Dependencies
            .Select(dependency => Path.Combine(
                Path.GetDirectoryName(dependency.DotoriPath)!, DotoriConstants.CacheDir,
                DotoriConstants.ObjSubDir, $"{targetId}-{config.ToLowerInvariant()}"))
            .ToArray();
        var allSwiftJob = planner.PlanSwiftCompileJob(
            checker: null, dependencyModuleDirectories: dependencyModuleDirectories);
        var swiftJob = planner.PlanSwiftCompileJob(
            checker, dependencyModuleDirectories,
            explain ? PrintCompileExplanation : null);
        if (swiftJob is not null)
        {
            var swiftResults = await executor.RunCompileJobsAsync(
                planner.GetSwiftCompilerPath(), [swiftJob], ct);
            var swiftCode = PrintResults(swiftResults);
            if (swiftCode != 0) return swiftCode;
            if (!dryRun)
            {
                checker.Record(swiftJob.SourceFile);
                foreach (var dependency in swiftJob.DependencyFiles) checker.Record(dependency);
                checker.RecordFingerprint(swiftJob.OutputFile, swiftJob.Fingerprint);
            }
        }

        // Keep the complete object set separate from the subset that needs compiling.
        // The linker must always receive every project object, not just changed sources.
        var allCompileJobs = planner.PlanCompileJobs(
            checker: null, pchPlan: pchPlan, bmiPaths: bmiPaths);
        var compileJobs = pchPlan?.BuildJob is not null
            ? allCompileJobs
            : planner.PlanCompileJobs(checker, pchPlan: pchPlan, bmiPaths: bmiPaths,
                explain: explain ? PrintCompileExplanation : null);
        traceProject?.RecordCompiles(
            allCompileJobs.Count + (allSwiftJob is null ? 0 : 1),
            compileJobs.Count + (swiftJob is null ? 0 : 1));
        // RC resource compilation (Windows MSVC only, runs before link)
        var rcJobs = planner.PlanRcJobs();
        if (rcJobs.Count > 0)
        {
            var rcResults = await executor.RunCompileJobsAsync(
                toolchain.Msvc!.RcPath!, rcJobs, ct);
            int rcCode = PrintResults(rcResults);
            if (rcCode != 0) return rcCode;
        }

        // External assembler jobs (NASM/YASM/GAS/MASM), runs before link
        var asmJobs = planner.PlanAssemblerJobs();
        if (asmJobs.Count > 0)
        {
            var asmPath = planner.GetAssemblerPath()!;
            var asmResults = await executor.RunCompileJobsAsync(asmPath, asmJobs, ct);
            int asmCode = PrintResults(asmResults);
            if (asmCode != 0) return asmCode;
        }

        if (compileJobs.Count > 0)
        {
            var compileResults = await executor.RunCompileJobsAsync(
                toolchain.CompilerPath, compileJobs, ct);

            int code = PrintResults(compileResults);
            if (code != 0) return code;

            if (!dryRun) foreach (var job in compileJobs)
            {
                checker.Record(job.SourceFile);
                foreach (var dependency in job.DependencyFiles)
                    checker.Record(dependency);
                checker.RecordFingerprint(job.OutputFile, job.Fingerprint);
            }
        }

        if (noLink) return 0;

        // Static libraries only contain .o files; executables/shared libs get .a files too
        var isStaticLib = model.Type == Dotori.Core.Parsing.ProjectType.StaticLibrary;
        // For Clang modules: .pcm files must also be linked
        var modulePcmFiles = (bmiPaths is not null && toolchain.Kind != Dotori.Core.Toolchain.CompilerKind.Msvc)
            ? bmiPaths.Values.ToList()
            : Enumerable.Empty<string>();
        var objFiles = allCompileJobs.Select(j => j.OutputFile)
            .Concat(allSwiftJob is null ? [] : new[] { allSwiftJob.OutputFile })
            .Concat(modulePcmFiles)
            .Concat(rcJobs.Select(j => j.OutputFile))   // .res files → linker
            .Concat(asmJobs.Select(j => j.OutputFile))  // assembler .o/.obj → linker
            .Concat(isStaticLib ? Enumerable.Empty<string>() : depLibs)
            .ToList();
        var linkJob  = planner.PlanLinkJob(objFiles);
        if (linkJob is null) return 0;

        var linkOutputExists = File.Exists(linkJob.OutputFile);
        var outputTimestamp = linkOutputExists
            ? File.GetLastWriteTimeUtc(linkJob.OutputFile)
            : DateTime.MinValue;
        var inputIsNewer = linkJob.InputFiles.Any(path =>
            File.Exists(path) && File.GetLastWriteTimeUtc(path) > outputTimestamp);
        var linkReasons = new List<string>();
        if (!linkOutputExists) linkReasons.Add("output is missing");
        if (compileJobs.Count > 0) linkReasons.Add($"{compileJobs.Count} source file(s) require compilation");
        if (swiftJob is not null) linkReasons.Add("Swift sources require compilation");
        if (moduleJobs.Count > 0) linkReasons.Add($"{moduleJobs.Count} module(s) were built");
        if (rcJobs.Count > 0) linkReasons.Add($"{rcJobs.Count} resource file(s) were built");
        if (asmJobs.Count > 0) linkReasons.Add($"{asmJobs.Count} assembler file(s) were built");
        if (inputIsNewer) linkReasons.Add("an input is newer than the output");
        if (checker.IsFingerprintChanged(linkJob.OutputFile, linkJob.Fingerprint))
            linkReasons.Add("linker command or input set changed");
        var needsLink = linkReasons.Count > 0;

        if (explain)
        {
            var detail = needsLink ? string.Join("; ", linkReasons) : "up to date";
            Console.WriteLine($"  [explain] link {linkJob.OutputFile}: {detail}");
        }

        if (!needsLink)
        {
            traceProject?.RecordLink(executed: false);
            builtLibraries[node.DotoriPath] = linkJob.OutputFile;
            Console.WriteLine($"  {model.Name}: up to date");
            return 0;
        }

        // Updating an existing archive leaves members for sources that were removed.
        // Recreate local static libraries so the archive exactly matches the input set.
        if (isStaticLib && executor is LocalExecutor && File.Exists(linkJob.OutputFile))
            File.Delete(linkJob.OutputFile);

        var linkResult = await executor.RunLinkJobAsync(planner.GetLinkerPath(), linkJob, ct);
        traceProject?.RecordLink(executed: true);
        if (!string.IsNullOrWhiteSpace(linkResult.Stdout)) Console.Write(linkResult.Stdout);
        if (!linkResult.Success)
        {
            if (!string.IsNullOrWhiteSpace(linkResult.Stderr)) Console.Error.Write(linkResult.Stderr);
            Console.Error.WriteLine($"Link failed (exit {linkResult.ExitCode})");
            return 1;
        }

        Console.WriteLine($"  Linked: {linkJob.OutputFile}");
        builtLibraries[node.DotoriPath] = linkJob.OutputFile;
        if (!dryRun) checker.RecordFingerprint(linkJob.OutputFile, linkJob.Fingerprint);

        // Manifest embedding (Windows MSVC only, runs after link)
        if (!dryRun && !await planner.EmbedManifestAsync(linkJob.OutputFile, ct))
            return 1;

        // Copy artifacts to user-specified output directories
        if (!dryRun) planner.CopyArtifacts(linkJob.OutputFile);

        // Copy files declared in copy { } blocks (incremental)
        if (!dryRun) planner.CopyCopyItems(checker);

        // post-build scripts
        if (model.PostBuildCommands.Count > 0 && !dryRun)
        {
            var postCode = await BuildContext.RunScriptsAsync(
                model.PostBuildCommands, model.ProjectDir,
                targetId, config, linkOutDir, ct);
            if (postCode != 0) return postCode;
        }

        return 0;
    }

    private static IReadOnlyList<string> CollectDepLibs(
        ProjectNode node,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> builtLibraries)
    {
        var libs = new List<string>();
        CollectDepLibsRecursive(node, builtLibraries, libs,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return libs;
    }

    private static void PrintCompileExplanation(
        CompileJob job, IReadOnlyList<string> reasons) =>
        Console.WriteLine($"  [explain] compile {job.SourceFile}: {string.Join("; ", reasons)}");

    private static void CollectDepLibsRecursive(
        ProjectNode node,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> builtLibraries,
        List<string> libs,
        HashSet<string> visited)
    {
        foreach (var dep in node.Dependencies)
        {
            if (!visited.Add(dep.DotoriPath)) continue;
            if (builtLibraries.TryGetValue(dep.DotoriPath, out var libPath))
                libs.Add(libPath);
            CollectDepLibsRecursive(dep, builtLibraries, libs, visited);
        }
    }

    private static int PrintResults(IReadOnlyList<JobResult> results)
    {
        foreach (var r in results)
        {
            if (!string.IsNullOrWhiteSpace(r.Stdout)) Console.Write(r.Stdout);
            if (!string.IsNullOrWhiteSpace(r.Stderr)) Console.Error.Write(r.Stderr);
            if (!r.Success)
            {
                Console.Error.WriteLine($"Compilation failed (exit {r.ExitCode})");
                return 1;
            }
        }
        return 0;
    }
}
