using System.CommandLine;
using System.Diagnostics;
using Dotori.Core.Generators;

namespace Dotori.Cli.Commands;

internal static class SetupCommandFactory
{
    private static readonly HashSet<string> SupportedIdes = new(
        ["auto", "all", "vscode", "zed", "clion", "rider", "visual-studio"],
        StringComparer.OrdinalIgnoreCase);

    public static Command Create()
    {
        var command = new Command("setup", "Set up integrations for external tools");
        command.Add(CreateIdeCommand());
        return command;
    }

    private static Command CreateIdeCommand()
    {
        var command = new Command("ide", "Generate CMake, compilation database, and editor integration files");
        var ideOption = new Option<string>("--ide")
        {
            Description = "IDE to configure: auto, all, vscode, zed, clion, rider, visual-studio",
            DefaultValueFactory = _ => "auto",
        };
        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var allOption = new Option<bool>("--all") { Description = "Include all projects" };
        var releaseOption = new Option<bool>("--release") { Description = "Use Release configuration for compile_commands.json" };
        var targetOption = new Option<string?>("--target") { Description = "Build target (default: host target)" };
        var compilerOption = new Option<string?>("--compiler") { Description = "Compiler to use (msvc, clang, brew-llvm, etc.)" };

        command.Add(ideOption);
        command.Add(projectOption);
        command.Add(allOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(compilerOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var ide = parseResult.GetValue(ideOption)!;
            if (!SupportedIdes.Contains(ide))
            {
                Console.Error.WriteLine($"error: unsupported IDE '{ide}'. Available: {string.Join(", ", SupportedIdes)}");
                return 1;
            }

            var projectArg = parseResult.GetValue(projectOption);
            var all = parseResult.GetValue(allOption);
            var paths = BuildContext.ResolveProjectPaths(projectArg, all);
            if (paths.Count == 0) return 1;

            var rootDir = Path.GetDirectoryName(Path.GetFullPath(paths[0]))!;
            var targetId = BuildContext.ResolveTargetId(parseResult.GetValue(targetOption));
            var compiler = parseResult.GetValue(compilerOption);

            var buildSystemFormats = ide.Equals("visual-studio", StringComparison.OrdinalIgnoreCase)
                ? new[] { "vcxproj" }
                : ide.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                  targetId.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "cmake", "vcxproj" }
                    : new[] { "cmake" };

            foreach (var format in buildSystemFormats)
            {
                var buildSystemArgs = new List<string>
                {
                    "export", "build-system", "--format", format,
                    "--project", paths[0], "--config", "both", "--target", targetId,
                };
                if (compiler is not null)
                {
                    buildSystemArgs.Add("--compiler");
                    buildSystemArgs.Add(compiler);
                }
                var buildSystemExitCode = await RunSelfAsync(buildSystemArgs, ct);
                if (buildSystemExitCode != 0) return buildSystemExitCode;
            }

            var ideArgs = new List<string>
            {
                "export", "ide", "--project", paths[0], "--target", targetId,
                "--output", Path.Combine(rootDir, "compile_commands.json"),
                "--clangd-output", Path.Combine(rootDir, ".clangd"),
            };
            if (all) ideArgs.Add("--all");
            if (parseResult.GetValue(releaseOption)) ideArgs.Add("--release");
            if (compiler is not null) { ideArgs.Add("--compiler"); ideArgs.Add(compiler); }
            var exitCode = await RunSelfAsync(ideArgs, ct);
            if (exitCode != 0) return exitCode;

            foreach (var (relativePath, content) in IdeIntegrationGenerator.Generate(ide))
            {
                var outputPath = Path.Combine(rootDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, content + Environment.NewLine, ct);
                Console.WriteLine($"Generated '{outputPath}'");
            }

            Console.WriteLine($"IDE setup complete for '{ide}' in '{rootDir}'.");
            return 0;
        });

        return command;
    }

    private static async Task<int> RunSelfAsync(IReadOnlyList<string> arguments, CancellationToken ct)
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
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
