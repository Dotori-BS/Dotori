using System.CommandLine;
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
        var checkOption = new Option<bool>("--check") { Description = "Check whether generated IDE files are present and current" };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Show files that would be generated without writing them" };

        command.Add(ideOption);
        command.Add(projectOption);
        command.Add(allOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(checkOption);
        command.Add(dryRunOption);

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

            var check = parseResult.GetValue(checkOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            if (check && dryRun)
            {
                Console.Error.WriteLine("error: --check and --dry-run cannot be used together.");
                return 1;
            }
            if (check || dryRun)
            {
                var dag = BuildContext.BuildDag(paths);
                if (dag is null) return 1;
                return InspectSetup(rootDir, dag.Keys.ToList(), ide, buildSystemFormats, check);
            }

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
                var buildSystemExitCode = await CliProcessRunner.RunSelfAsync(buildSystemArgs, ct);
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
            var exitCode = await CliProcessRunner.RunSelfAsync(ideArgs, ct);
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

    private static int InspectSetup(
        string rootDir,
        IReadOnlyList<string> projectPaths,
        string ide,
        IReadOnlyList<string> buildSystemFormats,
        bool check)
    {
        var sourceTime = projectPaths.Max(path => File.GetLastWriteTimeUtc(path));
        var expected = new List<(string Label, string? Path, string? Content)>();
        if (buildSystemFormats.Contains("cmake"))
        {
            expected.Add(("CMakeLists.txt", Path.Combine(rootDir, "CMakeLists.txt"), null));
            expected.Add(("CMakePresets.json", Path.Combine(rootDir, "CMakePresets.json"), null));
        }
        if (buildSystemFormats.Contains("vcxproj"))
        {
            expected.Add(("Visual Studio solution", Directory.EnumerateFiles(rootDir, "*.sln").FirstOrDefault(), null));
            expected.Add(("Visual Studio project", Directory.EnumerateFiles(rootDir, "*.vcxproj").FirstOrDefault(), null));
        }
        expected.Add(("compile_commands.json", Path.Combine(rootDir, "compile_commands.json"), null));
        expected.Add((".clangd", Path.Combine(rootDir, ".clangd"), null));
        expected.AddRange(IdeIntegrationGenerator.Generate(ide)
            .Select(file => (file.RelativePath, (string?)Path.Combine(rootDir, file.RelativePath), (string?)file.Content)));

        var invalid = false;
        foreach (var (label, path, content) in expected)
        {
            if (path is null || !File.Exists(path))
            {
                Console.WriteLine($"[{(check ? "missing" : "would generate")}] {label}");
                invalid = true;
            }
            else if (File.GetLastWriteTimeUtc(path) < sourceTime)
            {
                Console.WriteLine($"[{(check ? "stale" : "would update")}] {label}");
                invalid = true;
            }
            else if (content is not null && Normalize(File.ReadAllText(path)) != Normalize(content))
            {
                Console.WriteLine($"[{(check ? "changed" : "would update")}] {label}");
                invalid = true;
            }
            else
            {
                Console.WriteLine($"[ok] {label}");
            }
        }

        if (!check)
            Console.WriteLine("Dry run complete; no files were written.");
        return check && invalid ? 1 : 0;
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
