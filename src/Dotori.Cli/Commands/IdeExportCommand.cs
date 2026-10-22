using System.CommandLine;
using Dotori.Core.Build;
using Dotori.Core.Graph;

namespace Dotori.Cli.Commands;

internal static class IdeExportCommandFactory
{
    public static Command Create(bool legacy = false)
    {
        var command = legacy
            ? new Command("generate-compile-commands", "Generate compile_commands.json for IDE integration (deprecated: use 'export ide')")
            : new Command("ide", "Export IDE integration files (compile_commands.json + .clangd)");

        var projectOption      = new Option<string?>("--project")        { Description = "Path to .dotori file or directory" };
        var allOption          = new Option<bool>("--all")               { Description = legacy ? "Include all projects (default: true)" : "Include all projects" };
        var releaseOption      = new Option<bool>("--release")           { Description = "Use Release configuration" };
        var targetOption       = new Option<string?>("--target")         { Description = legacy ? "Build target (default: host target)" : "Build target (e.g. macos-arm64)" };
        var compilerOption     = new Option<string?>("--compiler")       { Description = "Compiler to use (msvc, clang)" };
        var runtimeLinkOption  = new Option<string?>("--runtime-link")   { Description = "Runtime link mode (static, dynamic)" };
        var libcOption         = new Option<string?>("--libc")           { Description = "C runtime library (glibc, musl)" };
        var stdlibOption       = new Option<string?>("--stdlib")         { Description = "C++ standard library (libc++, libstdc++)" };
        var outputOption       = new Option<string?>("--output")         { Description = legacy ? "Output path (default: compile_commands.json in project root)" : "Output path (default: compile_commands.json)" };
        var clangdOption       = new Option<bool>("--clangd")            { Description = "Generate .clangd config file (default: true)", DefaultValueFactory = _ => true };
        var noClangdOption     = new Option<bool>("--no-clangd")         { Description = "Do not generate a .clangd config file" };
        var clangdOutputOption = new Option<string?>("--clangd-output")  { Description = "Output path for .clangd (default: same directory as compile_commands.json)" };

        command.Add(projectOption);
        command.Add(allOption);
        if (!legacy)
            command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(runtimeLinkOption);
        command.Add(libcOption);
        command.Add(stdlibOption);
        command.Add(outputOption);
        command.Add(clangdOption);
        command.Add(noClangdOption);
        command.Add(clangdOutputOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg     = parseResult.GetValue(projectOption);
            var buildAll       = parseResult.GetValue(allOption);
            var release        = !legacy && parseResult.GetValue(releaseOption);
            var targetArg      = parseResult.GetValue(targetOption);
            var compiler       = parseResult.GetValue(compilerOption);
            var runtimeLink    = parseResult.GetValue(runtimeLinkOption);
            var libc           = parseResult.GetValue(libcOption);
            var stdlib         = parseResult.GetValue(stdlibOption);
            var outputArg      = parseResult.GetValue(outputOption);
            var generateClangd = parseResult.GetValue(clangdOption) &&
                                  !parseResult.GetValue(noClangdOption);
            var clangdOutputArg = parseResult.GetValue(clangdOutputOption);

            var config   = release ? "release" : "debug";
            var targetId = BuildContext.ResolveTargetId(targetArg);

            // Resolve project paths
            var paths = BuildContext.ResolveProjectPaths(
                projectArg, buildAll || (legacy && projectArg is null));
            if (paths.Count == 0) return 1;

            // Build DAG
            var dag = BuildContext.BuildDag(paths);
            if (dag is null) return 1;

            // Detect toolchain (CLI --compiler > DSL toolchain > CXX env > auto-detect)
            var toolchain = BuildContext.DetectToolchain(
                targetId, compiler, paths.FirstOrDefault(), libc);
            if (toolchain is null) return 1;

            var ctx      = BuildContext.MakeTargetContext(targetId, config, runtimeLink, libc, stdlib);
            var allEntries = new List<CompileCommandEntry>();

            // Walk all nodes in DAG and collect entries
            var buildOrder = ProjectDagBuilder.BuildLevels(dag);
            foreach (var level in buildOrder)
            {
                foreach (var node in level)
                {
                    var model = BuildContext.FlattenProject(node.DotoriPath, ctx, node);
                    if (model is null)
                    {
                        Console.Error.WriteLine($"Warning: Could not flatten '{node.DotoriPath}', skipping.");
                        continue;
                    }

                    var entries = await CompileCommandsExporter.GenerateEntriesAsync(model, toolchain, config, targetId, ct);
                    allEntries.AddRange(entries);
                }
            }

            // Determine output path
            string outputPath;
            if (outputArg is not null)
            {
                outputPath = Path.IsPathRooted(outputArg)
                    ? outputArg
                    : Path.GetFullPath(outputArg);
            }
            else
            {
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "compile_commands.json");
            }

            await CompileCommandsExporter.WriteAsync(outputPath, allEntries, ct);
            var verb = legacy ? "Generated" : "Exported";
            Console.WriteLine($"{verb} {allEntries.Count} entries to '{outputPath}'");

            if (generateClangd)
            {
                var clangdPath = clangdOutputArg is not null
                    ? (Path.IsPathRooted(clangdOutputArg) ? clangdOutputArg : Path.GetFullPath(clangdOutputArg))
                    : Path.Combine(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory(), ".clangd");
                await ClangdConfigGenerator.WriteAsync(clangdPath, toolchain.CompilerPath, ct);
                Console.WriteLine($"Generated .clangd at '{clangdPath}'");
            }

            return 0;
        });

        return command;
    }
}
