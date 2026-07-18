using System.CommandLine;
using System.Diagnostics;
using Dotori.Core.Build;

namespace Dotori.Cli.Commands;

/// <summary>
/// Builds the project and runs the resulting executable as a test binary.
/// Arguments after <c>--</c> are forwarded verbatim.
/// </summary>
internal static class TestCommandFactory
{
    public static Command Create()
    {
        var command = new Command("test", "Build and run tests");

        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var releaseOption = new Option<bool>("--release") { Description = "Test in Release configuration" };
        var targetOption = new Option<string?>("--target") { Description = "Build target" };
        var filterOption = new Option<string?>("--filter") { Description = "Test name filter pattern" };
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
            Description = "Arguments to pass to the test executable",
            AllowMultipleArgumentsPerToken = true,
        };

        command.Add(projectOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(filterOption);
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
            var runArgs = new List<string>(parseResult.GetValue(argsOption) ?? []);
            var filter = parseResult.GetValue(filterOption);
            if (filter is not null)
                runArgs.Add($"--filter={filter}");

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0)
                return 1;

            var build = await ProjectBuildEngine.BuildExecutableAsync(paths, targetId, config, ct);
            if (build.ExitCode != 0)
                return build.ExitCode;
            if (build.ExecutablePath is null)
            {
                Console.Error.WriteLine("Error: No executable project found to test.");
                return 1;
            }

            Console.WriteLine();
            if (debuggerArg is not null)
                return await RunCommandFactory.LaunchDebuggerAsync(
                    targetId, debuggerArg, build.ExecutablePath, runArgs, debuggerArgs, ct);

            var startInfo = new ProcessStartInfo(build.ExecutablePath)
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
                    Console.Error.WriteLine($"Failed to start '{build.ExecutablePath}'");
                    return 1;
                }

                await process.WaitForExitAsync(ct);
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error running tests '{build.ExecutablePath}': {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}
