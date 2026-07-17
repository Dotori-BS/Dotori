using System.CommandLine;
using System.Text;
using Dotori.Core.Parsing;
using Dotori.PackageManager;

namespace Dotori.Cli.Commands;

/// <summary>
/// Factory for package management CLI commands (add / remove / update / list).
/// Split into partial files:
///   PackageCommands.cs         — command factory methods (Create, CreateAdd/Remove/Update/List)
///   PackageCommands.Helpers.cs — DSL text manipulation and lock-file helpers
/// </summary>
internal static partial class PackageCommandFactory
{
    public static Command Create()
    {
        var packageCommand = new Command("package", "Package management commands (aliases: dotori add/remove/update/list)");

        packageCommand.Add(CreateAddCommand());
        packageCommand.Add(CreateRemoveCommand());
        packageCommand.Add(CreateUpdateCommand());
        packageCommand.Add(CreateListCommand());

        return packageCommand;
    }

    // ─── Individual factory methods (also used as top-level commands) ─────────

    public static Command CreateAddCommand()
    {
        var command = new Command("add", "Add a dependency to the project");

        var nameArg    = new Argument<string>("name")    { Description = "Package name (optionally with @version), git URL, or local path" };
        var gitOption  = new Option<bool>("--git")       { Description = "Treat name as a git URL" };
        var pathOption = new Option<bool>("--path")      { Description = "Treat name as a local path" };
        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };

        command.Add(nameArg);
        command.Add(gitOption);
        command.Add(pathOption);
        command.Add(projectOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var nameArg2   = parseResult.GetValue(nameArg);
            var isGit      = parseResult.GetValue(gitOption);
            var isPath     = parseResult.GetValue(pathOption);
            var projectArg = parseResult.GetValue(projectOption);

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0) return 1;
            var dotoriPath = paths[0];
            var projectDir = Path.GetDirectoryName(dotoriPath)!;

            // Parse name@version or plain name
            string depName;
            string depLine;

            if (isPath)
            {
                // Local path dependency
                depName = Path.GetFileName(nameArg2!.TrimEnd('/', '\\'));
                depLine = $"    {depName} = {{ path = \"{nameArg2}\" }}";
            }
            else if (isGit)
            {
                // Git URL dependency (no version info at this point)
                depName = ExtractGitName(nameArg2!);
                depLine = $"    {depName} = {{ git = \"{nameArg2}\" }}";
            }
            else
            {
                // name or name@version
                var atIdx = nameArg2!.LastIndexOf('@');
                if (atIdx > 0)
                {
                    depName = nameArg2[..atIdx];
                    var version = nameArg2[(atIdx + 1)..];
                    depLine = $"    {depName} = \"{version}\"";
                }
                else
                {
                    depName = nameArg2;
                    depLine = $"    {depName} = \"*\"";
                }
            }

            // Modify the .dotori file text
            var text = File.ReadAllText(dotoriPath, Encoding.UTF8);
            var modified = InsertDependency(text, depLine);
            if (modified is null)
            {
                Console.Error.WriteLine($"Error: could not find project block to insert dependency in '{dotoriPath}'.");
                return 1;
            }

            File.WriteAllText(dotoriPath, modified, Encoding.UTF8);
            Console.WriteLine($"  Added {depName} to {dotoriPath}");

            // Re-resolve lock file
            return await UpdateLockAsync(dotoriPath, projectDir, null, ct);
        });

        return command;
    }

    public static Command CreateRemoveCommand()
    {
        var command = new Command("remove", "Remove a dependency from the project");

        var nameArg       = new Argument<string>("name")     { Description = "Package name to remove" };
        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };

        command.Add(nameArg);
        command.Add(projectOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var depName    = parseResult.GetValue(nameArg);
            var projectArg = parseResult.GetValue(projectOption);

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0) return 1;
            var dotoriPath = paths[0];
            var projectDir = Path.GetDirectoryName(dotoriPath)!;

            var text = File.ReadAllText(dotoriPath, Encoding.UTF8);
            var modified = RemoveDependency(text, depName!);
            if (modified is null)
            {
                Console.Error.WriteLine($"Error: dependency '{depName}' not found in '{dotoriPath}'.");
                return 1;
            }

            File.WriteAllText(dotoriPath, modified, Encoding.UTF8);
            Console.WriteLine($"  Removed {depName} from {dotoriPath}");

            // Re-resolve lock file (remove from lock too)
            return await UpdateLockAsync(dotoriPath, projectDir, null, ct);
        });

        return command;
    }

    public static Command CreateUpdateCommand()
    {
        var command = new Command("update", "Update dependencies (re-resolve lock file)");

        var nameArg       = new Argument<string?>("name")
        {
            Description = "Package name to update (omit for all)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };

        command.Add(nameArg);
        command.Add(projectOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var specificName = parseResult.GetValue(nameArg);
            var projectArg   = parseResult.GetValue(projectOption);

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0) return 1;

            int exitCode = 0;
            foreach (var dotoriPath in paths)
            {
                var projectDir = Path.GetDirectoryName(dotoriPath)!;
                exitCode = await UpdateLockAsync(dotoriPath, projectDir, specificName, ct);
                if (exitCode != 0) break;
            }
            return exitCode;
        });

        return command;
    }

    public static Command CreateListCommand()
    {
        var command = new Command("list", "List declared dependencies");

        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        command.Add(projectOption);

        command.SetAction((parseResult) =>
        {
            var projectArg = parseResult.GetValue(projectOption);

            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0) return 1;

            foreach (var dotoriPath in paths)
            {
                try
                {
                    var file = DotoriParser.ParseFile(dotoriPath);
                    if (file.Project is null)
                    {
                        Console.Error.WriteLine($"Error: No project declaration in '{dotoriPath}'.");
                        return 1;
                    }

                    var deps = CollectAllDeps(file.Project);
                    if (deps.Count == 0)
                    {
                        Console.WriteLine($"{file.Project.Name}: (no dependencies)");
                        return 0;
                    }

                    Console.WriteLine($"{file.Project.Name}:");
                    foreach (var dep in deps)
                    {
                        var valueStr = dep.Value switch
                        {
                            VersionDependency v  => v.Version,
                            ComplexDependency c when c.Path is not null  => $"path = {c.Path}",
                            ComplexDependency c when c.Git  is not null  => $"git = {c.Git}" + (c.Tag is not null ? $", tag = {c.Tag}" : c.Commit is not null ? $", commit = {c.Commit}" : ""),
                            ComplexDependency c when c.Version is not null => c.Version,
                            _ => "?",
                        };
                        Console.WriteLine($"  {dep.Name,-30} {valueStr}");
                    }
                }
                catch (ParseException ex)
                {
                    Console.Error.WriteLine($"Parse error: {ex.Message}");
                    return 1;
                }
            }
            return 0;
        });

        return command;
    }
}
