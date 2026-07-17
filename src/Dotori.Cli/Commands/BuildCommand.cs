using System.CommandLine;
using Dotori.Core;
using Dotori.Core.Build;
using Dotori.Core.Executor;
using Dotori.Core.Graph;

namespace Dotori.Cli.Commands;

internal static class BuildCommandFactory
{
    public static Command Create()
    {
        var command = new Command("build", "Build the project");

        var projectOption     = new Option<string?>("--project")      { Description = "Path to .dotori file or directory" };
        var allOption         = new Option<bool>("--all")             { Description = "Build all projects without prompt" };
        var releaseOption     = new Option<bool>("--release")         { Description = "Build in Release configuration" };
        var targetOption      = new Option<string?>("--target")       { Description = "Build target (e.g. windows-x64, linux-x64)" };
        var compilerOption    = new Option<string?>("--compiler")     { Description = "Compiler to use: 'msvc', 'clang', a binary name (e.g. clang++-18), or an absolute/relative path. Overrides the CXX/CC environment variables." };
        var runtimeLinkOption = new Option<string?>("--runtime-link") { Description = "Runtime link mode (static, dynamic)" };
        var libcOption        = new Option<string?>("--libc")         { Description = "C runtime library (glibc, musl)" };
        var stdlibOption      = new Option<string?>("--stdlib")       { Description = "C++ standard library (libc++, libstdc++)" };
        var jobsOption        = new Option<int?>("--jobs")            { Description = "Number of parallel jobs" };
        var fileOption        = new Option<string?>("--file")         { Description = "Build a single source file" };
        var noLinkOption      = new Option<bool>("--no-link")         { Description = "Compile only, do not link" };
        var noUnityOption     = new Option<bool>("--no-unity")        { Description = "Bypass Unity Build for --file (compile file directly)" };
        var remoteOption      = new Option<string?>("--remote")       { Description = "Remote build server address (e.g. http://build-server:5100)" };
        var lockedOption      = new Option<bool>("--locked")         { Description = "Require dotori-lock.json to remain unchanged" };
        var offlineOption     = new Option<bool>("--offline")        { Description = "Disable dependency network access" };
        var frozenOption      = new Option<bool>("--frozen")         { Description = "Equivalent to --locked --offline" };
        var dryRunOption      = new Option<bool>("--dry-run")        { Description = "Print build commands without executing them" };
        var explainOption     = new Option<bool>("--explain")        { Description = "Explain why compile and link steps run" };

        command.Add(projectOption);
        command.Add(allOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(runtimeLinkOption);
        command.Add(libcOption);
        command.Add(stdlibOption);
        command.Add(jobsOption);
        command.Add(fileOption);
        command.Add(noLinkOption);
        command.Add(noUnityOption);
        command.Add(remoteOption);
        command.Add(lockedOption);
        command.Add(offlineOption);
        command.Add(frozenOption);
        command.Add(dryRunOption);
        command.Add(explainOption);

        // Allow unknown --flags so project-declared options can be passed as --option-name / --no-option-name
        command.TreatUnmatchedTokensAsErrors = false;

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg  = parseResult.GetValue(projectOption);
            var buildAll    = parseResult.GetValue(allOption);
            var release     = parseResult.GetValue(releaseOption);
            var targetArg   = parseResult.GetValue(targetOption);
            var compiler    = parseResult.GetValue(compilerOption);
            var runtimeLink = parseResult.GetValue(runtimeLinkOption);
            var libc        = parseResult.GetValue(libcOption);
            var stdlib      = parseResult.GetValue(stdlibOption);
            var jobs        = parseResult.GetValue(jobsOption);
            var fileArg     = parseResult.GetValue(fileOption);
            var noLink      = parseResult.GetValue(noLinkOption);
            var noUnity     = parseResult.GetValue(noUnityOption);
            var remoteArg   = parseResult.GetValue(remoteOption)
                           ?? Environment.GetEnvironmentVariable(DotoriConstants.EnvServer);
            var frozen     = parseResult.GetValue(frozenOption);
            var locked     = frozen || parseResult.GetValue(lockedOption);
            var offline    = frozen || parseResult.GetValue(offlineOption);
            var dryRun     = parseResult.GetValue(dryRunOption);
            var explain    = parseResult.GetValue(explainOption);

            var config   = release ? "release" : "debug";
            var targetId = BuildContext.ResolveTargetId(targetArg);

            // Resolve project paths
            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll);
            if (paths.Count == 0) return 1;

            // Resolve and fetch git dependencies, then include them in the DAG
            IReadOnlyDictionary<string, string> gitPackageMap;
            try
            {
                gitPackageMap = await BuildContext.ResolveAndFetchGitPackagesAsync(
                    paths, locked, offline, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: dependency resolution failed: {ex.Message}");
                return 1;
            }

            // Build DAG (includes git package nodes when their .dotori is found)
            var dag = BuildContext.BuildDag(paths, gitPackageMap);
            if (dag is null) return 1;

            // Get build order
            var buildOrder = ProjectDagBuilder.BuildLevels(dag);

            // Detect toolchain (CLI --compiler > DSL toolchain > CXX env > auto-detect)
            var toolchain = BuildContext.DetectToolchain(
                targetId, compiler, paths.FirstOrDefault(), libc);
            if (toolchain is null) return 1;

            // Scan declared options across all project files in the DAG
            var allDotoriPaths = dag.Values.Select(n => n.DotoriPath);
            var declaredOptions = BuildContext.ScanOptions(allDotoriPaths);

            // Parse unmatched tokens for --option-name / --no-option-name
            var cliEnabled  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cliDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in parseResult.UnmatchedTokens)
            {
                if (token.StartsWith("--no-", StringComparison.Ordinal))
                {
                    cliDisabled.Add(token[5..]);
                }
                else if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    cliEnabled.Add(token[2..]);
                }
                else
                {
                    Console.Error.WriteLine($"Error: Unexpected argument '{token}'.");
                    return 1;
                }
            }

            // Validate: all CLI option tokens must match a declared option
            var allCliOptions = cliEnabled.Concat(cliDisabled);
            foreach (var name in allCliOptions)
            {
                if (!declaredOptions.ContainsKey(name))
                {
                    var known = declaredOptions.Count > 0
                        ? $" Known options: {string.Join(", ", declaredOptions.Keys)}"
                        : " No options are declared in any project.";
                    Console.Error.WriteLine($"Error: Unknown option '--{name}'.{known}");
                    return 1;
                }
            }

            // Resolve final enabled options (defaults + CLI overrides)
            var enabledOptions = BuildContext.ResolveEnabledOptions(declaredOptions, cliEnabled, cliDisabled);

            var ctx = BuildContext.MakeTargetContext(
                targetId, config, runtimeLink, libc, stdlib,
                enabledOptions, declaredOptions);

            // Select executor: remote (with local fallback) or local
            IExecutor executor = dryRun
                ? new DryRunExecutor()
                : remoteArg is not null
                ? new RemoteExecutor(remoteArg, toolchain.TargetTriple, jobs ?? 0)
                : new LocalExecutor(jobs ?? 0);

            if (remoteArg is not null && !dryRun)
                Console.WriteLine($"  Using remote build server: {remoteArg}");

            int exitCode = 0;
            // Track dotoriPath → built library output (for transitive linking)
            var builtLibraries = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            // Track whether --file was found in any project (array trick for async/Interlocked)
            int[] fileFound = [0]; // 0 = not found, 1 = found

            try
            {
                foreach (var level in buildOrder)
                {
                    // Compile all projects in this level in parallel
                    var levelTasks = level.Select(node => BuildProjectAsync(
                        node, ctx, toolchain, executor, targetId, config,
                        fileArg, noLink, noUnity, dryRun, explain, builtLibraries, fileFound, ct));

                    var levelResults = await Task.WhenAll(levelTasks);
                    if (levelResults.Any(r => r != 0)) { exitCode = 1; break; }
                }
            }
            finally
            {
                if (executor is IDisposable disposable)
                    disposable.Dispose();
            }

            // If --file was specified but not found in any project, report error
            if (fileArg is not null && exitCode == 0 && fileFound[0] == 0)
            {
                Console.Error.WriteLine($"Error: '{fileArg}' is not in any project sources.");
                return 1;
            }

            return exitCode;
        });

        return command;
    }

    private static async Task<int> BuildProjectAsync(
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

        // Keep the complete object set separate from the subset that needs compiling.
        // The linker must always receive every project object, not just changed sources.
        var allCompileJobs = planner.PlanCompileJobs(
            checker: null, pchPlan: pchPlan, bmiPaths: bmiPaths);
        var compileJobs = pchPlan?.BuildJob is not null
            ? allCompileJobs
            : planner.PlanCompileJobs(checker, pchPlan: pchPlan, bmiPaths: bmiPaths,
                explain: explain ? PrintCompileExplanation : null);
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
            builtLibraries[node.DotoriPath] = linkJob.OutputFile;
            Console.WriteLine($"  {model.Name}: up to date");
            return 0;
        }

        // Updating an existing archive leaves members for sources that were removed.
        // Recreate local static libraries so the archive exactly matches the input set.
        if (isStaticLib && executor is LocalExecutor && File.Exists(linkJob.OutputFile))
            File.Delete(linkJob.OutputFile);

        var linkResult = await executor.RunLinkJobAsync(planner.GetLinkerPath(), linkJob, ct);
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
