using System.CommandLine;
using Dotori.Core;
using Dotori.Core.Build;
using Dotori.Core.Parsing;

namespace Dotori.Cli.Commands;

internal static class CleanCommandFactory
{
    public static Command Create()
    {
        var command = new Command("clean", "Remove build artifacts");

        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var allOption     = new Option<bool>("--all")        { Description = "Remove all cached build artifacts (use --deps to also remove fetched packages)" };
        var depsOption    = new Option<bool>("--deps")       { Description = "Also remove the deps/ directory containing fetched packages" };
        var dryRunOption  = new Option<bool>("--dry-run")    { Description = "Show what would be removed without deleting it" };

        command.Add(projectOption);
        command.Add(allOption);
        command.Add(depsOption);
        command.Add(dryRunOption);

        command.SetAction((parseResult) =>
        {
            var projectArg = parseResult.GetValue(projectOption);
            var cleanAll   = parseResult.GetValue(allOption);
            var cleanDeps  = parseResult.GetValue(depsOption);
            var dryRun     = parseResult.GetValue(dryRunOption);

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: cleanAll);

            foreach (var dotoriPath in paths)
            {
                var projectDir = Path.GetDirectoryName(dotoriPath)!;

                var cacheDir = Path.Combine(projectDir, DotoriConstants.CacheDir);

                // Remove files copied by copy { } blocks (using manifest — read before cacheDir is deleted)
                var manifestPath = Path.Combine(cacheDir, DotoriConstants.CopyManifestFileName);
                if (File.Exists(manifestPath))
                {
                    var manifest = BuildPlanner.LoadManifest(manifestPath);
                    foreach (var destPath in manifest.Values)
                    {
                        if (File.Exists(destPath))
                        {
                            Console.WriteLine($"  Removing {destPath}");
                            if (!dryRun) File.Delete(destPath);
                        }
                    }
                }

                // Remove .dotori-cache/
                if (Directory.Exists(cacheDir))
                {
                    Console.WriteLine($"  Removing {cacheDir}");
                    if (!dryRun) Directory.Delete(cacheDir, recursive: true);
                }

                // Remove output directories declared in output { } blocks
                try
                {
                    var file = DotoriParser.ParseFile(dotoriPath);
                    if (file.Project is not null)
                    {
                        foreach (var dir in CollectOutputPaths(file.Project, projectDir))
                        {
                            if (Directory.Exists(dir))
                            {
                                if (!PathUtils.IsSafeCleanDirectory(projectDir, dir))
                                {
                                    Console.Error.WriteLine(
                                        $"Warning: refusing to recursively remove output outside the project: {dir}");
                                    continue;
                                }
                                Console.WriteLine($"  Removing {dir}");
                                if (!dryRun) Directory.Delete(dir, recursive: true);
                            }
                        }
                    }
                }
                catch { /* ignore parse errors during clean */ }
            }

            // Remove deps/ directories (fetched packages)
            if (cleanDeps)
            {
                foreach (var dotoriPath in paths)
                {
                    var projectDir = Path.GetDirectoryName(dotoriPath)!;
                    var depsDir = Path.Combine(projectDir, DotoriConstants.DepsDir);
                    if (Directory.Exists(depsDir))
                    {
                        Console.WriteLine($"  Removing {depsDir}");
                        if (!dryRun) Directory.Delete(depsDir, recursive: true);
                    }
                }
            }

            Console.WriteLine(dryRun ? "  Clean dry run complete." : "  Clean complete.");
            return 0;
        });

        return command;
    }

    private static IEnumerable<string> CollectOutputPaths(ProjectDecl project, string projectDir)
        => CollectOutputPathsFromItems(project.Items, projectDir);

    private static IEnumerable<string> CollectOutputPathsFromItems(
        IEnumerable<ProjectItem> items, string projectDir)
    {
        foreach (var item in items)
        {
            if (item is OutputBlock ob)
            {
                if (ob.Binaries  is not null) yield return ResolveDir(ob.Binaries,  projectDir);
                if (ob.Libraries is not null) yield return ResolveDir(ob.Libraries, projectDir);
                if (ob.Symbols   is not null) yield return ResolveDir(ob.Symbols,   projectDir);
            }
            else if (item is ConditionBlock cb)
            {
                foreach (var p in CollectOutputPathsFromItems(cb.Items, projectDir))
                    yield return p;
            }
        }
    }

    private static string ResolveDir(string path, string projectDir)
        => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(projectDir, path));
}
