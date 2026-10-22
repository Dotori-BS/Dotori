using System.CommandLine;
using Dotori.Core;
using Dotori.Core.Build;
using Dotori.Core.Executor;
using Dotori.Grpc;

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
        var verboseOption     = new Option<bool>("--verbose")        { Description = "Print compile and link commands as they execute" };
        var traceOption       = new Option<string?>("--trace")       { Description = "Write a structured JSON build trace" };
        var timingsOption     = new Option<bool>("--timings")        { Description = "Print build timing and incremental reuse statistics" };

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
        command.Add(verboseOption);
        command.Add(traceOption);
        command.Add(timingsOption);

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
            var verbose    = parseResult.GetValue(verboseOption);
            var tracePath  = parseResult.GetValue(traceOption);
            var timings    = parseResult.GetValue(timingsOption);

            var config   = release ? "release" : "debug";
            var targetId = BuildContext.ResolveTargetId(targetArg);

            // Resolve project paths
            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll);
            if (paths.Count == 0) return 1;

            var prepared = await BuildContext.PrepareBuildAsync(
                paths, targetId, compiler, libc, locked, offline, ct);
            if (prepared is null) return 1;
            var buildOrder = prepared.Projects.BuildOrder;
            var toolchain = prepared.Toolchain;

            // Scan declared options across all project files in the DAG
            var allDotoriPaths = prepared.Projects.Nodes.Values.Select(n => n.DotoriPath);
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
            if (verbose && !dryRun)
                executor = new VerboseExecutor(executor);

            if (remoteArg is not null && !dryRun)
                Console.WriteLine($"  Using remote build server: {remoteArg}");

            // Track whether --file was found in any project (array trick for async/Interlocked)
            int[] fileFound = [0]; // 0 = not found, 1 = found

            ProjectBuildResult result;
            var trace = tracePath is not null || timings
                ? new BuildTraceSession(targetId, config)
                : null;
            try
            {
                result = await ProjectBuildEngine.BuildAsync(
                    buildOrder, ctx, toolchain, executor, targetId, config,
                    fileArg, noLink, noUnity, dryRun, explain, fileFound, trace, ct);
            }
            finally
            {
                if (executor is IDisposable disposable)
                    disposable.Dispose();
            }

            if (trace is not null)
            {
                var document = trace.Finish();
                if (tracePath is not null)
                {
                    try
                    {
                        BuildTraceSession.WriteJson(document, tracePath);
                        Console.WriteLine($"  Trace: {Path.GetFullPath(tracePath)}");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Console.Error.WriteLine($"Error: unable to write build trace: {ex.Message}");
                        return 1;
                    }
                }
                if (timings)
                {
                    var summary = document.Summary;
                    Console.WriteLine(
                        $"  Timing: {document.DurationMs:F1} ms, " +
                        $"projects {summary.SucceededProjects}/{summary.ProjectCount}, " +
                        $"compiled {summary.CompiledInputs}/{summary.CompileInputs}, " +
                        $"reuse {summary.ReusePercent:F2}%");
                }
            }
            // If --file was specified but not found in any project, report error
            if (fileArg is not null && result.ExitCode == 0 && fileFound[0] == 0)
            {
                Console.Error.WriteLine($"Error: '{fileArg}' is not in any project sources.");
                return 1;
            }

            return result.ExitCode;
        });

        return command;
    }
}
