using System.CommandLine;
using System.Text.Json;
using Dotori.Core.Build;

namespace Dotori.Cli.Commands;

internal static class InstallCommandFactory
{
    public static Command CreateInstall()
    {
        var command = new Command("install", "Install built artifacts and public headers");
        var options = AddCommonOptions(command);
        var compilerOption = new Option<string?>("--compiler")
            { Description = "Compiler/toolchain used to locate the built artifact" };
        var libcOption = new Option<string?>("--libc") { Description = "C runtime library" };
        command.Add(compilerOption);
        command.Add(libcOption);

        command.SetAction((parseResult) =>
        {
            var values = ReadCommon(parseResult, options);
            var paths = BuildContext.ResolveProjectPaths(values.Project, values.All);
            if (paths.Count == 0) return 1;
            var toolchain = BuildContext.DetectToolchain(
                values.TargetId, parseResult.GetValue(compilerOption), paths[0],
                parseResult.GetValue(libcOption));
            if (toolchain is null) return 1;
            var context = BuildContext.MakeTargetContext(
                values.TargetId, values.Config, libc: parseResult.GetValue(libcOption));

            foreach (var path in paths)
            {
                var model = BuildContext.FlattenProject(path, context);
                if (model is null) return 1;
                try
                {
                    var planner = new BuildPlanner(model, toolchain, values.Config, values.TargetId);
                    var artifact = planner.PlanLinkJob([])?.OutputFile;
                    var files = InstallManager.PlanInstall(
                        model, artifact, values.Prefix, values.DestDir);
                    foreach (var file in files)
                        Console.WriteLine($"  Installing {file.Source} -> {file.Destination}");
                    if (!values.DryRun)
                        InstallManager.Install(model, artifact, values.Prefix, values.DestDir,
                            values.TargetId, values.Config);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
                {
                    Console.Error.WriteLine($"Install failed for '{model.Name}': {ex.Message}");
                    return 1;
                }
            }
            return 0;
        });
        return command;
    }

    public static Command CreateUninstall()
    {
        var command = new Command("uninstall", "Remove files recorded by an install manifest");
        var options = AddCommonOptions(command);
        command.SetAction((parseResult) =>
        {
            var values = ReadCommon(parseResult, options);
            var paths = BuildContext.ResolveProjectPaths(values.Project, values.All);
            if (paths.Count == 0) return 1;
            var context = BuildContext.MakeTargetContext(values.TargetId, values.Config);
            foreach (var path in paths)
            {
                var model = BuildContext.FlattenProject(path, context);
                if (model is null) return 1;
                try
                {
                    var files = InstallManager.PlanUninstall(
                        model.Name, values.Prefix, values.DestDir,
                        values.TargetId, values.Config);
                    foreach (var file in files) Console.WriteLine($"  Removing {file}");
                    if (!values.DryRun)
                        InstallManager.Uninstall(model.Name, values.Prefix, values.DestDir,
                            values.TargetId, values.Config);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
                {
                    Console.Error.WriteLine($"Uninstall failed for '{model.Name}': {ex.Message}");
                    return 1;
                }
            }
            return 0;
        });
        return command;
    }

    private static CommonOptions AddCommonOptions(Command command)
    {
        var project = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var all = new Option<bool>("--all") { Description = "Process all discovered projects" };
        var release = new Option<bool>("--release") { Description = "Use Release configuration" };
        var target = new Option<string?>("--target") { Description = "Build target" };
        var prefix = new Option<string>("--prefix")
        {
            Description = "Installation prefix",
            DefaultValueFactory = _ => DefaultPrefix(),
        };
        var destDir = new Option<string?>("--destdir")
        {
            Description = "Staging root prepended to the installation prefix",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("DESTDIR"),
        };
        var dryRun = new Option<bool>("--dry-run") { Description = "Show changes without writing or deleting files" };
        command.Add(project);
        command.Add(all);
        command.Add(release);
        command.Add(target);
        command.Add(prefix);
        command.Add(destDir);
        command.Add(dryRun);
        return new CommonOptions(project, all, release, target, prefix, destDir, dryRun);
    }

    private static CommonValues ReadCommon(
        ParseResult result, CommonOptions options)
    {
        var release = result.GetValue(options.Release);
        return new CommonValues(
            result.GetValue(options.Project), result.GetValue(options.All),
            release ? "release" : "debug",
            BuildContext.ResolveTargetId(result.GetValue(options.Target)),
            result.GetValue(options.Prefix)!, result.GetValue(options.DestDir),
            result.GetValue(options.DryRun));
    }

    private static string DefaultPrefix() =>
        Environment.GetEnvironmentVariable("DOTORI_INSTALL_PREFIX")
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dotori")
            : "/usr/local");

    private sealed record CommonOptions(
        Option<string?> Project, Option<bool> All, Option<bool> Release,
        Option<string?> Target, Option<string> Prefix, Option<string?> DestDir,
        Option<bool> DryRun);

    private sealed record CommonValues(
        string? Project, bool All, string Config, string TargetId,
        string Prefix, string? DestDir, bool DryRun);
}
