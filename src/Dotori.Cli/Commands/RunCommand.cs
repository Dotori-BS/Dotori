using System.CommandLine;
using System.Diagnostics;
using Dotori.Core.Build;
using Dotori.Core.Debugger;
using Dotori.Core.Parsing;

namespace Dotori.Cli.Commands;

internal static class RunCommandFactory
{
    public static Command Create()
    {
        var command = new Command("run", "Build and run the project");

        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var releaseOption = new Option<bool>("--release") { Description = "Run in Release configuration" };
        var targetOption = new Option<string?>("--target") { Description = "Build target (e.g. macos-arm64)" };
        var debuggerOption = new Option<string?>("--debugger")
        {
            Description = "Run under debugger. Specify kind (lldb, gdb, vsdbg, windbg, cdb) or leave empty for auto-detection",
        };
        var debuggerArgsOption = new Option<string[]>("--debugger-args")
        {
            Description = "Additional debugger arguments",
            AllowMultipleArgumentsPerToken = true,
        };
        var argsOption = new Option<string[]>("--")
        {
            Description = "Arguments to pass to the executable",
            AllowMultipleArgumentsPerToken = true,
        };

        command.Add(projectOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(debuggerOption);
        command.Add(debuggerArgsOption);
        command.Add(argsOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg = parseResult.GetValue(projectOption);
            var config = parseResult.GetValue(releaseOption) ? "release" : "debug";
            var targetId = BuildContext.ResolveTargetId(parseResult.GetValue(targetOption));
            var debuggerArg = parseResult.GetValue(debuggerOption);
            var debuggerArgs = parseResult.GetValue(debuggerArgsOption) ?? [];
            var runArgs = parseResult.GetValue(argsOption) ?? [];

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0)
                return 1;

            var build = await ProjectBuildEngine.BuildRunnablesAsync(
                paths, targetId, config, ProjectType.Executable, ct: ct);
            if (build.ExitCode != 0)
                return build.ExitCode;
            var executablePath = build.ExecutablePaths.LastOrDefault();
            if (executablePath is null)
            {
                Console.Error.WriteLine("Error: No executable project found to run.");
                return 1;
            }

            Console.WriteLine();
            if (debuggerArg is not null)
                return await LaunchDebuggerAsync(
                    targetId, debuggerArg, executablePath, runArgs, debuggerArgs, ct);

            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
            };
            foreach (var argument in runArgs)
                startInfo.ArgumentList.Add(argument);

            try
            {
                var process = Process.Start(startInfo);
                if (process is null)
                {
                    Console.Error.WriteLine($"Failed to start '{executablePath}'");
                    return 1;
                }

                await process.WaitForExitAsync(ct);
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error running '{executablePath}': {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    internal static async Task<int> LaunchDebuggerAsync(
        string targetId,
        string debuggerArg,
        string executablePath,
        IReadOnlyList<string> runArgs,
        IReadOnlyList<string> debuggerArgs,
        CancellationToken ct)
    {
        var validationError = DebuggerDetector.ValidateDebugCompatibility(targetId);
        if (validationError is not null)
        {
            Console.Error.WriteLine($"Error: {validationError}");
            return 1;
        }

        DebuggerInfo? debuggerInfo;
        if (string.IsNullOrEmpty(debuggerArg))
        {
            debuggerInfo = DebuggerDetector.DetectDefault();
        }
        else if (Enum.TryParse<DebuggerKind>(debuggerArg, true, out var kind))
        {
            debuggerInfo = DebuggerDetector.Detect(kind);
        }
        else
        {
            Console.Error.WriteLine($"Error: Unknown debugger '{debuggerArg}'");
            Console.Error.WriteLine("Available: lldb, gdb, vsdbg, windbg, cdb");
            return 1;
        }

        if (debuggerInfo is null)
        {
            var message = string.IsNullOrEmpty(debuggerArg)
                ? "Error: No debugger found. Install lldb, gdb, or windbg."
                : $"Error: Debugger '{debuggerArg}' not found.";
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine($"Launching under {debuggerInfo.DisplayName ?? debuggerInfo.Kind.ToString()}...");
        Console.WriteLine();
        return await DebuggerLauncher.LaunchAsync(
            debuggerInfo, executablePath, runArgs, debuggerArgs, ct);
    }
}
