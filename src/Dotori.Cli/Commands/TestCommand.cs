using System.CommandLine;
using System.Diagnostics;
using Dotori.Core;
using Dotori.Core.Build;
using Dotori.Core.Debugger;
using Dotori.Core.Executor;
using Dotori.Core.Graph;
using Dotori.Core.Parsing;

namespace Dotori.Cli.Commands;

/// <summary>
/// Build the project and run the resulting executable as a test binary.
/// Any arguments passed after `--` are forwarded to the test executable.
/// The --filter option appends a platform-agnostic filter argument (--filter VALUE).
/// </summary>
internal static class TestCommandFactory
{
    public static Command Create()
    {
        var command = new Command("test", "Build and run tests");

        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var releaseOption = new Option<bool>("--release")    { Description = "Test in Release configuration" };
        var targetOption  = new Option<string?>("--target")  { Description = "Build target" };
        var filterOption  = new Option<string?>("--filter")  { Description = "Test name filter pattern" };
        var debuggerOption = new Option<string?>("--debugger")
        {
            Description = "Run under debugger. Specify kind (lldb, gdb, vsdbg, windbg, cdb) or leave empty for auto-detection",
        };
        var debuggerArgsOption = new Option<string[]>("--debugger-args")
        {
            Description = "Additional debugger arguments",
            AllowMultipleArgumentsPerToken = true,
        };
        var argsOption    = new Option<string[]>("--")       { Description = "Arguments to pass to the test executable", AllowMultipleArgumentsPerToken = true };

        command.Add(projectOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(filterOption);
        command.Add(debuggerOption);
        command.Add(debuggerArgsOption);
        command.Add(argsOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg      = parseResult.GetValue(projectOption);
            var release         = parseResult.GetValue(releaseOption);
            var targetArg       = parseResult.GetValue(targetOption);
            var filter          = parseResult.GetValue(filterOption);
            var debuggerArg     = parseResult.GetValue(debuggerOption); // null if not specified, "" or kind if specified
            var debuggerArgsVal = parseResult.GetValue(debuggerArgsOption) ?? Array.Empty<string>();
            var extraArgs       = parseResult.GetValue(argsOption) ?? Array.Empty<string>();

            var config   = release ? "release" : "debug";
            var targetId = BuildContext.ResolveTargetId(targetArg);

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0) return 1;

            var dag = BuildContext.BuildDag(paths);
            if (dag is null) return 1;

            var buildOrder = ProjectDagBuilder.BuildLevels(dag);
            var toolchain  = BuildContext.DetectToolchain(targetId, cliCompiler: null, paths.FirstOrDefault());
            if (toolchain is null) return 1;

            var ctx      = BuildContext.MakeTargetContext(targetId, config);
            var executor = new LocalExecutor(0);
            var builtLibraries = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            string? testExePath = null;

            foreach (var level in buildOrder)
            {
                foreach (var node in level)
                {
                    var model = BuildContext.FlattenProject(node.DotoriPath, ctx, node);
                    if (model is null) return 1;

                    Console.WriteLine($"  Building {model.Name}...");

                    using var checker = new IncrementalChecker(model.ProjectDir, $"{targetId}-{config}");
                    var planner = new BuildPlanner(model, toolchain, config, targetId);

                    var linkOutDir = Path.Combine(model.ProjectDir, DotoriConstants.CacheDir,
                        DotoriConstants.BinSubDir, $"{targetId}-{config.ToLower()}");

                    // pre-build scripts
                    if (model.PreBuildCommands.Count > 0)
                    {
                        var preCode = await BuildContext.RunScriptsAsync(
                            model.PreBuildCommands, model.ProjectDir,
                            targetId, config, linkOutDir, ct);
                        if (preCode != 0) return preCode;
                    }

                    // PCH
                    var pchPlan = planner.PlanPch(checker);
                    if (pchPlan?.BuildJob is not null)
                    {
                        var pchResults = await executor.RunCompileJobsAsync(
                            toolchain.CompilerPath, new[] { pchPlan.BuildJob }, ct);
                        foreach (var r in pchResults)
                        {
                            if (!string.IsNullOrWhiteSpace(r.Stdout)) Console.Write(r.Stdout);
                            if (!string.IsNullOrWhiteSpace(r.Stderr)) Console.Error.Write(r.Stderr);
                            if (!r.Success)
                            {
                                Console.Error.WriteLine($"Build failed (exit {r.ExitCode})");
                                return 1;
                            }
                        }
                        checker.Record(pchPlan.BuildJob.SourceFile);
                    }

                    // Modules
                    var moduleJobs = await planner.PlanModuleJobsAsync(ct: ct);
                    IReadOnlyDictionary<string, string>? bmiPaths = null;
                    if (moduleJobs.Count > 0)
                    {
                        foreach (var bmiJob in moduleJobs)
                        {
                            var bmiResults = await executor.RunCompileJobsAsync(
                                toolchain.CompilerPath, new[] { bmiJob }, ct);
                            foreach (var r in bmiResults)
                            {
                                if (!string.IsNullOrWhiteSpace(r.Stdout)) Console.Write(r.Stdout);
                                if (!string.IsNullOrWhiteSpace(r.Stderr)) Console.Error.Write(r.Stderr);
                                if (!r.Success)
                                {
                                    Console.Error.WriteLine($"Build failed (exit {r.ExitCode})");
                                    return 1;
                                }
                            }
                        }
                        bmiPaths = BuildPlanner.ExtractBmiPaths(moduleJobs);
                        planner.WriteModuleMap(moduleJobs);
                    }

                    // Compile
                    var compileJobs = planner.PlanCompileJobs(checker, pchPlan: pchPlan, bmiPaths: bmiPaths);
                    if (compileJobs.Count > 0)
                    {
                        var results = await executor.RunCompileJobsAsync(
                            toolchain.CompilerPath, compileJobs, ct);
                        foreach (var r in results)
                        {
                            if (!string.IsNullOrWhiteSpace(r.Stdout)) Console.Write(r.Stdout);
                            if (!string.IsNullOrWhiteSpace(r.Stderr)) Console.Error.Write(r.Stderr);
                            if (!r.Success)
                            {
                                Console.Error.WriteLine($"Build failed (exit {r.ExitCode})");
                                return 1;
                            }
                        }
                        foreach (var j in compileJobs) checker.Record(j.SourceFile);
                    }

                    // Collect transitive dependency library paths
                    var depLibs = CollectDepLibs(node, builtLibraries);

                    var isStaticLib = model.Type == ProjectType.StaticLibrary;
                    var modulePcmFiles = (bmiPaths is not null && toolchain.Kind != Dotori.Core.Toolchain.CompilerKind.Msvc)
                        ? bmiPaths.Values
                        : Enumerable.Empty<string>();
                    var objFiles = compileJobs.Count > 0
                        ? compileJobs.Select(j => j.OutputFile)
                            .Concat(modulePcmFiles)
                            .Concat(isStaticLib ? Enumerable.Empty<string>() : depLibs)
                            .ToList()
                        : Directory.Exists(Path.Combine(model.ProjectDir, DotoriConstants.CacheDir,
                              DotoriConstants.ObjSubDir, $"{targetId}-{config.ToLower()}"))
                            ? Directory.GetFiles(Path.Combine(model.ProjectDir, DotoriConstants.CacheDir,
                              DotoriConstants.ObjSubDir, $"{targetId}-{config.ToLower()}"), "*.o")
                              .Concat(Directory.GetFiles(Path.Combine(model.ProjectDir,
                                  DotoriConstants.CacheDir, DotoriConstants.ObjSubDir, $"{targetId}-{config.ToLower()}"), "*.obj"))
                              .Concat(modulePcmFiles)
                              .Concat(isStaticLib ? Enumerable.Empty<string>() : depLibs)
                              .ToList()
                            : new List<string>();

                    var linkJob = planner.PlanLinkJob(objFiles);
                    if (linkJob is not null)
                    {
                        var linkResult = await executor.RunLinkJobAsync(planner.GetLinkerPath(), linkJob, ct);
                        if (!linkResult.Success)
                        {
                            Console.Error.WriteLine(linkResult.Stderr);
                            return 1;
                        }
                        Console.WriteLine($"  Linked: {linkJob.OutputFile}");
                        builtLibraries[node.DotoriPath] = linkJob.OutputFile;

                        planner.CopyArtifacts(linkJob.OutputFile);

                        if (model.PostBuildCommands.Count > 0)
                        {
                            var postCode = await BuildContext.RunScriptsAsync(
                                model.PostBuildCommands, model.ProjectDir,
                                targetId, config, linkOutDir, ct);
                            if (postCode != 0) return postCode;
                        }

                        if (model.Type == ProjectType.Executable)
                            testExePath = linkJob.OutputFile;
                    }
                }
            }

            if (testExePath is null)
            {
                Console.Error.WriteLine("Error: No executable project found to test.");
                return 1;
            }

            // Build test arguments
            var runArgs = new List<string>(extraArgs);
            if (filter is not null)
                runArgs.Add($"--filter={filter}");

            Console.WriteLine();

            // *** Debugger mode handling ***
            if (debuggerArg is not null)
            {
                // 1. Validate compatibility
                var validationError = DebuggerDetector.ValidateDebugCompatibility(targetId);
                if (validationError is not null)
                {
                    Console.Error.WriteLine($"Error: {validationError}");
                    return 1;
                }

                // 2. Select debugger
                DebuggerInfo? debuggerInfo = null;
                if (string.IsNullOrEmpty(debuggerArg))
                {
                    // Auto-detect (--debugger with no value or --debugger "")
                    debuggerInfo = DebuggerDetector.DetectDefault();
                }
                else
                {
                    // Explicit kind specified (--debugger lldb)
                    if (Enum.TryParse<DebuggerKind>(debuggerArg, true, out var kind))
                        debuggerInfo = DebuggerDetector.Detect(kind);
                    else
                    {
                        Console.Error.WriteLine($"Error: Unknown debugger '{debuggerArg}'");
                        Console.Error.WriteLine("Available: lldb, gdb, vsdbg, windbg, cdb");
                        return 1;
                    }
                }

                if (debuggerInfo is null)
                {
                    var msg = string.IsNullOrEmpty(debuggerArg)
                        ? "Error: No debugger found. Install lldb, gdb, or windbg."
                        : $"Error: Debugger '{debuggerArg}' not found.";
                    Console.Error.WriteLine(msg);
                    return 1;
                }

                Console.WriteLine($"Launching under {debuggerInfo.DisplayName ?? debuggerInfo.Kind.ToString()}...");
                Console.WriteLine();

                return await DebuggerLauncher.LaunchAsync(
                    debuggerInfo,
                    testExePath,
                    runArgs,
                    debuggerArgsVal,
                    ct);
            }

            // *** Normal execution ***
            var psi = new ProcessStartInfo(testExePath, string.Join(" ", runArgs))
            {
                UseShellExecute = false,
            };

            try
            {
                var proc = Process.Start(psi);
                if (proc is null)
                {
                    Console.Error.WriteLine($"Failed to start '{testExePath}'");
                    return 1;
                }
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error running tests '{testExePath}': {ex.Message}");
                return 1;
            }
        });

        return command;
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
}
