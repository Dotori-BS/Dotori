using System.Diagnostics;

namespace Dotori.Cli;

internal static class CliProcessRunner
{
    internal static Process StartSelf(
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        bool createNoWindow = false,
        Action<string>? onStandardOutput = null,
        Action<string>? onStandardError = null)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current Dotori executable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            CreateNoWindow = createNoWindow,
            RedirectStandardOutput = onStandardOutput is not null,
            RedirectStandardError = onStandardError is not null,
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Environment.GetCommandLineArgs().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(assemblyPath) ||
                !assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                assemblyPath = Path.Combine(AppContext.BaseDirectory, "dotori.dll");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Dotori CLI process.");
        if (onStandardOutput is not null)
        {
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null) onStandardOutput(eventArgs.Data);
            };
            process.BeginOutputReadLine();
        }
        if (onStandardError is not null)
        {
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null) onStandardError(eventArgs.Data);
            };
            process.BeginErrorReadLine();
        }
        return process;
    }

    internal static async Task<int> RunSelfAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = StartSelf(arguments);
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
