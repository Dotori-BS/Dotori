using System.Diagnostics;

namespace Dotori.Cli;

internal static class CliProcessRunner
{
    internal static async Task<int> RunSelfAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current Dotori executable.");
        var startInfo = new ProcessStartInfo(processPath) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Environment.GetCommandLineArgs().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(assemblyPath) ||
                !assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                assemblyPath = Path.Combine(AppContext.BaseDirectory, "dotori.dll");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Dotori CLI process.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
