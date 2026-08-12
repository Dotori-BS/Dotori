using System.CommandLine;

namespace Dotori.Cli.Commands;

internal static class DoctorCommandFactory
{
    private static readonly HashSet<string> SupportedIdes = new(
        ["auto", "all", "vscode", "zed", "clion", "rider", "visual-studio"],
        StringComparer.OrdinalIgnoreCase);

    public static Command Create()
    {
        var command = new Command("doctor", "Diagnose the C/C++ toolchain and Dotori IDE setup");
        var ideOption = new Option<string>("--ide")
        {
            Description = "IDE to diagnose: auto, all, vscode, zed, clion, rider, visual-studio",
            DefaultValueFactory = _ => "auto",
        };
        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var targetOption = new Option<string?>("--target") { Description = "Build target (default: host target)" };
        var compilerOption = new Option<string?>("--compiler") { Description = "Compiler to use (msvc, clang, brew-llvm, etc.)" };
        var fixOption = new Option<bool>("--fix") { Description = "Regenerate missing or stale IDE files" };

        command.Add(ideOption);
        command.Add(projectOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(fixOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var ide = parseResult.GetValue(ideOption)!;
            if (!SupportedIdes.Contains(ide))
            {
                Console.Error.WriteLine($"error: unsupported IDE '{ide}'. Available: {string.Join(", ", SupportedIdes)}");
                return 1;
            }

            Console.WriteLine($"[ok] dotori: {Environment.ProcessPath ?? AppContext.BaseDirectory}");
            var toolsReady = ReportTool("cmake", required: !ide.Equals("visual-studio", StringComparison.OrdinalIgnoreCase));
            ReportTool("ninja", required: false);
            ReportTool("clangd", required: false);

            var paths = BuildContext.ResolveProjectPaths(parseResult.GetValue(projectOption), buildAll: false);
            if (paths.Count == 0) return 1;
            Console.WriteLine($"[ok] project: {Path.GetFullPath(paths[0])}");

            var targetId = BuildContext.ResolveTargetId(parseResult.GetValue(targetOption));
            var compiler = parseResult.GetValue(compilerOption);
            if (BuildContext.DetectToolchain(targetId, compiler, paths[0]) is null)
                return 1;
            Console.WriteLine($"[ok] compiler: {compiler ?? "auto-detected"} ({targetId})");
            if (!toolsReady) return 1;

            var setupArguments = new List<string>
            {
                "setup", "ide", "--ide", ide, "--project", paths[0], "--target", targetId,
            };
            if (compiler is not null)
            {
                setupArguments.Add("--compiler");
                setupArguments.Add(compiler);
            }

            if (parseResult.GetValue(fixOption))
            {
                Console.WriteLine("Regenerating IDE integration files...");
                return await CliProcessRunner.RunSelfAsync(setupArguments, ct);
            }

            setupArguments.Add("--check");
            var setupExitCode = await CliProcessRunner.RunSelfAsync(setupArguments, ct);
            if (setupExitCode != 0)
                Console.Error.WriteLine("IDE setup needs attention. Run 'dotori doctor --fix' to regenerate it.");
            else
                Console.WriteLine("Dotori development environment is ready.");
            return setupExitCode;
        });

        return command;
    }

    private static bool ReportTool(string name, bool required)
    {
        var path = FindOnPath(name);
        if (path is not null)
        {
            Console.WriteLine($"[ok] {name}: {path}");
            return true;
        }
        else
            Console.WriteLine($"[{(required ? "missing" : "warning")}] {name}: not found on PATH");
        return !required;
    }

    private static string? FindOnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions.Prepend(string.Empty))
            {
                var candidate = Path.Combine(directory.Trim('"'), name + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
