using System.Diagnostics;

namespace Dotori.Core.Build;

/// <summary>
/// Runs pre-build or post-build shell script commands.
/// </summary>
public static class ScriptRunner
{
    /// <summary>
    /// Run a list of build scripts in order.
    /// Each command is executed via the shell. Returns non-zero on first failure.
    /// </summary>
    /// <param name="commands">Commands to run.</param>
    /// <param name="projectDir">Working directory for each command.</param>
    /// <param name="targetId">Passed as DOTORI_TARGET env var.</param>
    /// <param name="config">Passed as DOTORI_CONFIG env var.</param>
    /// <param name="outputDir">Passed as DOTORI_OUTPUT_DIR env var.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> commands,
        string                projectDir,
        string                targetId,
        string                config,
        string                outputDir,
        CancellationToken     ct = default)
    {
        foreach (var cmd in commands)
        {
            string shell, shellArg;
            if (OperatingSystem.IsWindows())
            {
                shell    = "cmd.exe";
                shellArg = $"/c \"{cmd}\"";
            }
            else
            {
                shell    = "/bin/sh";
                shellArg = $"-c \"{cmd.Replace("\"", "\\\"")}\"";
            }

            var psi = new ProcessStartInfo(shell, shellArg)
            {
                WorkingDirectory       = projectDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.Environment["DOTORI_TARGET"]      = targetId;
            psi.Environment["DOTORI_CONFIG"]      = config;
            psi.Environment["DOTORI_PROJECT_DIR"] = projectDir;
            psi.Environment["DOTORI_OUTPUT_DIR"]  = outputDir;

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Console.Error.WriteLine($"Error: Failed to start script: {cmd}");
                return 1;
            }

            // Stream output in real time
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (!proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    try { await proc.WaitForExitAsync(CancellationToken.None); }
                    catch (InvalidOperationException) { }
                }
                throw;
            }
            // WaitForExit() (no-arg) ensures async stdout/stderr readers are fully drained
            // before we read ExitCode or let the caller delete the working directory.
            proc.WaitForExit();
            var exitCode = proc.ExitCode;
            if (exitCode != 0)
            {
                Console.Error.WriteLine($"Script failed (exit {exitCode}): {cmd}");
                return exitCode;
            }
        }
        return 0;
    }
}
