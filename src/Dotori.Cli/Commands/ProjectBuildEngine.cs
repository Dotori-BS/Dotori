using System.Collections.Concurrent;
using Dotori.Core.Executor;
using Dotori.Core.Graph;
using Dotori.Core.Model;
using Dotori.Core.Toolchain;

namespace Dotori.Cli.Commands;

internal sealed record ProjectBuildResult(
    int ExitCode,
    IReadOnlyDictionary<string, string> Outputs);

internal sealed record ExecutableBuildResult(int ExitCode, string? ExecutablePath);

/// <summary>
/// Executes a project DAG through the single build pipeline used by build, run, and test.
/// Command-specific behavior such as launching the resulting executable stays outside this type.
/// </summary>
internal static partial class ProjectBuildEngine
{
    public static async Task<ExecutableBuildResult> BuildExecutableAsync(
        IReadOnlyList<string> projectPaths,
        string targetId,
        string config,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, string> gitPackageMap;
        try
        {
            gitPackageMap = await BuildContext.ResolveAndFetchGitPackagesAsync(
                projectPaths, locked: false, offline: false, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: dependency resolution failed: {ex.Message}");
            return new ExecutableBuildResult(1, null);
        }

        var dag = BuildContext.BuildDag(projectPaths, gitPackageMap);
        if (dag is null)
            return new ExecutableBuildResult(1, null);

        var buildOrder = ProjectDagBuilder.BuildLevels(dag);
        var toolchain = BuildContext.DetectToolchain(
            targetId, cliCompiler: null, projectPaths.FirstOrDefault());
        if (toolchain is null)
            return new ExecutableBuildResult(1, null);

        var declaredOptions = BuildContext.ScanOptions(dag.Values.Select(node => node.DotoriPath));
        var enabledOptions = BuildContext.ResolveEnabledOptions(
            declaredOptions,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var context = BuildContext.MakeTargetContext(
            targetId, config, enabledOptions: enabledOptions,
            declaredOptions: declaredOptions);

        var executor = new LocalExecutor(0);
        var result = await BuildAsync(
            buildOrder, context, toolchain, executor, targetId, config, ct: ct);
        if (result.ExitCode != 0)
            return new ExecutableBuildResult(result.ExitCode, null);

        return new ExecutableBuildResult(
            0, FindLastExecutable(buildOrder, result.Outputs, context));
    }

    public static async Task<ProjectBuildResult> BuildAsync(
        IReadOnlyList<IReadOnlyList<ProjectNode>> buildOrder,
        TargetContext context,
        ToolchainInfo toolchain,
        IExecutor executor,
        string targetId,
        string config,
        string? fileArg = null,
        bool noLink = false,
        bool noUnity = false,
        bool dryRun = false,
        bool explain = false,
        int[]? fileFound = null,
        CancellationToken ct = default)
    {
        var outputs = new ConcurrentDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        fileFound ??= [0];

        foreach (var level in buildOrder)
        {
            var tasks = level.Select(node => BuildProjectAsync(
                node, context, toolchain, executor, targetId, config,
                fileArg, noLink, noUnity, dryRun, explain, outputs, fileFound, ct));

            var results = await Task.WhenAll(tasks);
            if (results.Any(code => code != 0))
                return new ProjectBuildResult(1, outputs);
        }

        return new ProjectBuildResult(0, outputs);
    }

    public static string? FindLastExecutable(
        IReadOnlyList<IReadOnlyList<ProjectNode>> buildOrder,
        IReadOnlyDictionary<string, string> outputs,
        TargetContext context)
    {
        string? executable = null;
        foreach (var node in buildOrder.SelectMany(level => level))
        {
            var model = BuildContext.FlattenProject(node.DotoriPath, context, node);
            if (model?.Type == Dotori.Core.Parsing.ProjectType.Executable &&
                outputs.TryGetValue(node.DotoriPath, out var output))
                executable = output;
        }

        return executable;
    }
}
