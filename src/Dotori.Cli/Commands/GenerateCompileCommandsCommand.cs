using System.CommandLine;
using Dotori.Core.Build;
using Dotori.Core.Graph;

namespace Dotori.Cli.Commands;

internal static class GenerateCompileCommandsCommandFactory
{
    public static Command Create()
    {
        var command = new Command("generate-compile-commands", "Generate compile_commands.json for IDE integration (deprecated: use 'export ide')");

        var projectOption      = new Option<string?>("--project")        { Description = "Path to .dotori file or directory" };
        var allOption          = new Option<bool>("--all")               { Description = "Include all projects (default: true)" };
        var targetOption       = new Option<string?>("--target")         { Description = "Build target (default: host target)" };
        var compilerOption     = new Option<string?>("--compiler")       { Description = "Compiler to use (msvc, clang)" };
        var runtimeLinkOption  = new Option<string?>("--runtime-link")   { Description = "Runtime link mode (static, dynamic)" };
        var libcOption         = new Option<string?>("--libc")           { Description = "C runtime library (glibc, musl)" };
        var stdlibOption       = new Option<string?>("--stdlib")         { Description = "C++ standard library (libc++, libstdc++)" };
        var outputOption       = new Option<string?>("--output")         { Description = "Output path (default: compile_commands.json in project root)" };
        var clangdOption       = new Option<bool>("--clangd")            { Description = "Generate .clangd config file (default: true)", DefaultValueFactory = _ => true };
        var clangdOutputOption = new Option<string?>("--clangd-output")  { Description = "Output path for .clangd (default: same directory as compile_commands.json)" };

        command.Add(projectOption);
        command.Add(allOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(runtimeLinkOption);
        command.Add(libcOption);
        command.Add(stdlibOption);
        command.Add(outputOption);
        command.Add(clangdOption);
        command.Add(clangdOutputOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg      = parseResult.GetValue(projectOption);
            var buildAll        = parseResult.GetValue(allOption);
            var targetArg       = parseResult.GetValue(targetOption);
            var compiler        = parseResult.GetValue(compilerOption);
            var runtimeLink     = parseResult.GetValue(runtimeLinkOption);
            var libc            = parseResult.GetValue(libcOption);
            var stdlib          = parseResult.GetValue(stdlibOption);
            var outputArg       = parseResult.GetValue(outputOption);
            var generateClangd  = parseResult.GetValue(clangdOption);
            var clangdOutputArg = parseResult.GetValue(clangdOutputOption);

            var targetId = BuildContext.ResolveTargetId(targetArg);

            // Default to debug configuration for IDE integration
            const string config = "debug";

            // Resolve project paths (default to --all behaviour)
            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll || projectArg is null);
            if (paths.Count == 0) return 1;

            // Build DAG
            var dag = BuildContext.BuildDag(paths);
            if (dag is null) return 1;

            // Detect toolchain (CLI --compiler > DSL toolchain > CXX env > auto-detect)
            var toolchain = BuildContext.DetectToolchain(
                targetId, compiler, paths.FirstOrDefault(), libc);
            if (toolchain is null) return 1;

            var ctx        = BuildContext.MakeTargetContext(targetId, config, runtimeLink, libc, stdlib);
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
            Console.WriteLine($"Generated {allEntries.Count} entries to '{outputPath}'");

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
