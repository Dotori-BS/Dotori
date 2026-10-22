using System.CommandLine;
using System.Diagnostics;
using Dotori.LanguageServer;

namespace Dotori.Cli.Commands;

internal static class LspCommandFactory
{
    public static Command Create()
    {
        var command = new Command("lsp", "Start the Dotori Language Server (LSP, stdio transport)");

        var logFileOption = new Option<string?>("--log-file")
        {
            Description = "Path to write LSP server log output",
        };
        var watchIdeOption = new Option<bool>("--watch-ide")
        {
            Description = "Keep compile_commands.json and .clangd current while the editor is open",
        };

        command.Add(logFileOption);
        command.Add(watchIdeOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var logFile = parseResult.GetValue(logFileOption);
            var watchIde = parseResult.GetValue(watchIdeOption);
            var watchProcesses = new List<Process>();
            try
            {
                await DotoriLanguageServer.RunAsync(logFile, ct, watchIde
                    ? roots => StartIdeWatchers(roots, watchProcesses)
                    : null);
                return Environment.ExitCode;
            }
            finally
            {
                foreach (var process in watchProcesses)
                {
                    try
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                    catch (InvalidOperationException) { /* already exited */ }
                    catch (System.ComponentModel.Win32Exception) { /* already exited */ }
                    finally { process.Dispose(); }
                }
            }
        });

        return command;
    }

    private static void StartIdeWatchers(IReadOnlyList<string> roots, List<Process> processes)
    {
        foreach (var root in roots)
        {
            if (processes.Any(process => string.Equals(
                    process.StartInfo.WorkingDirectory, root,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                continue;

            try
            {
                var process = CliProcessRunner.StartSelf(
                    ["watch", "--clangd", "--project", root, "--all", "--debounce", "750"],
                    workingDirectory: root,
                    createNoWindow: true,
                    onStandardOutput: line => Console.Error.WriteLine($"[dotori-ide-watch] {line}"),
                    onStandardError: line => Console.Error.WriteLine($"[dotori-ide-watch] {line}"));
                processes.Add(process);
            }
            catch (Exception ex) when (ex is InvalidOperationException or
                                        System.ComponentModel.Win32Exception or IOException or ArgumentException)
            {
                Console.Error.WriteLine($"[dotori-ide-watch] Could not start for '{root}': {ex.Message}");
            }
        }
    }
}
