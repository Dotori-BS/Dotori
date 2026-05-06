using System.CommandLine;
using Dotori.Core.Build;
using Dotori.Core.Graph;

namespace Dotori.Cli.Commands;

internal static class ExportCommandFactory
{
    public static Command Create()
    {
        var exportCommand = new Command("export", "Export project information");

        exportCommand.Add(CreateCompileCommandsSubcommand());
        exportCommand.Add(ExportGrammarCommandFactory.Create());
        exportCommand.Add(ExportBuildSystemCommandFactory.Create());

        return exportCommand;
    }

    private static Command CreateCompileCommandsSubcommand()
    {
        var command = new Command("compile-commands", "Export compile_commands.json for IDE integration");

        var projectOption     = new Option<string?>("--project")      { Description = "Path to .dotori file or directory" };
        var allOption         = new Option<bool>("--all")             { Description = "Include all projects" };
        var releaseOption     = new Option<bool>("--release")         { Description = "Use Release configuration" };
        var targetOption      = new Option<string?>("--target")       { Description = "Build target (e.g. macos-arm64)" };
        var compilerOption    = new Option<string?>("--compiler")     { Description = "Compiler to use (msvc, clang)" };
        var runtimeLinkOption = new Option<string?>("--runtime-link") { Description = "Runtime link mode (static, dynamic)" };
        var libcOption        = new Option<string?>("--libc")         { Description = "C runtime library (glibc, musl)" };
        var stdlibOption      = new Option<string?>("--stdlib")       { Description = "C++ standard library (libc++, libstdc++)" };
        var outputOption      = new Option<string?>("--output")       { Description = "Output path (default: compile_commands.json)" };

        command.Add(projectOption);
        command.Add(allOption);
        command.Add(releaseOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(runtimeLinkOption);
        command.Add(libcOption);
        command.Add(stdlibOption);
        command.Add(outputOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg  = parseResult.GetValue(projectOption);
            var buildAll    = parseResult.GetValue(allOption);
            var release     = parseResult.GetValue(releaseOption);
            var targetArg   = parseResult.GetValue(targetOption);
            var compiler    = parseResult.GetValue(compilerOption);
            var runtimeLink = parseResult.GetValue(runtimeLinkOption);
            var libc        = parseResult.GetValue(libcOption);
            var stdlib      = parseResult.GetValue(stdlibOption);
            var outputArg   = parseResult.GetValue(outputOption);

            var config   = release ? "release" : "debug";
            var targetId = BuildContext.ResolveTargetId(targetArg);

            // Resolve project paths
            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll);
            if (paths.Count == 0) return 1;

            // Build DAG
            var dag = BuildContext.BuildDag(paths);
            if (dag is null) return 1;

            // Detect toolchain
            var toolchain = BuildContext.DetectToolchain(targetId, compiler);
            if (toolchain is null) return 1;

            var ctx      = BuildContext.MakeTargetContext(targetId, config, runtimeLink, libc, stdlib);
            var allEntries = new List<Dotori.Core.Build.CompileCommandEntry>();

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
                // Default: use current working directory (works for both single and multi-project)
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "compile_commands.json");
            }

            await CompileCommandsExporter.WriteAsync(outputPath, allEntries, ct);
            Console.WriteLine($"Exported {allEntries.Count} entries to '{outputPath}'");

            return 0;
        });

        return command;
    }
}
