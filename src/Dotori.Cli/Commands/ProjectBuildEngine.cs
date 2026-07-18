using System.Collections.Concurrent;
using Dotori.Core.Build;
using Dotori.Core.Executor;
using Dotori.Core.Graph;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Cli.Commands;

internal sealed record ProjectBuildResult(
    int ExitCode,
    IReadOnlyDictionary<string, string> Outputs);

internal sealed record RunnableBuildResult(int ExitCode, IReadOnlyList<string> ExecutablePaths);

/// <summary>
/// Executes a project DAG through the single build pipeline used by build, run, and test.
/// Command-specific behavior such as launching the resulting executable stays outside this type.
/// </summary>
internal static partial class ProjectBuildEngine
{
    public static async Task<RunnableBuildResult> BuildRunnablesAsync(
        IReadOnlyList<string> projectPaths,
        string targetId,
        string config,
        ProjectType projectType,
        bool fallbackToExecutable = false,
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
            return new RunnableBuildResult(1, []);
        }

        var dag = BuildContext.BuildDag(projectPaths, gitPackageMap);
        if (dag is null)
            return new RunnableBuildResult(1, []);

        var buildOrder = ProjectDagBuilder.BuildLevels(dag);
        var toolchain = BuildContext.DetectToolchain(
            targetId, cliCompiler: null, projectPaths.FirstOrDefault());
        if (toolchain is null)
            return new RunnableBuildResult(1, []);

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
            return new RunnableBuildResult(result.ExitCode, []);

        var paths = FindOutputs(buildOrder, result.Outputs, context, projectType);
        if (paths.Count == 0 && fallbackToExecutable && projectType != ProjectType.Executable)
            paths = FindOutputs(buildOrder, result.Outputs, context, ProjectType.Executable);
        return new RunnableBuildResult(0, paths);
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
        BuildTraceSession? trace = null,
        CancellationToken ct = default)
    {
        var outputs = new ConcurrentDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        fileFound ??= [0];

        foreach (var level in buildOrder)
        {
            var tasks = level.Select(BuildNodeAsync);

            var results = await Task.WhenAll(tasks);
            if (results.Any(code => code != 0))
                return new ProjectBuildResult(1, outputs);
        }

        return new ProjectBuildResult(0, outputs);

        async Task<int> BuildNodeAsync(ProjectNode node)
        {
            var projectTrace = trace?.BeginProject(node.ProjectName, node.DotoriPath);
            var code = await BuildProjectAsync(
                node, context, toolchain, executor, targetId, config,
                fileArg, noLink, noUnity, dryRun, explain, outputs, fileFound,
                projectTrace, ct);
            projectTrace?.Complete(code == 0);
            return code;
        }
    }

    public static IReadOnlyList<string> FindOutputs(
        IReadOnlyList<IReadOnlyList<ProjectNode>> buildOrder,
        IReadOnlyDictionary<string, string> outputs,
        TargetContext context,
        ProjectType projectType)
    {
        var matches = new List<string>();
        foreach (var node in buildOrder.SelectMany(level => level))
        {
            var model = BuildContext.FlattenProject(node.DotoriPath, context, node);
            if (model?.Type == projectType &&
                outputs.TryGetValue(node.DotoriPath, out var output))
                matches.Add(output);
        }

        return matches;
    }
}
