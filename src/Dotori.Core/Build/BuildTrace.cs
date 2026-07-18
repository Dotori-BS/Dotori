using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotori.Core.Build;

public sealed record BuildTraceDocument
{
    public int SchemaVersion { get; init; } = 1;
    public required string Target { get; init; }
    public required string Configuration { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset FinishedUtc { get; init; }
    public required double DurationMs { get; init; }
    public required BuildTraceSummary Summary { get; init; }
    public required List<BuildTraceProjectRecord> Projects { get; init; }
}

public sealed record BuildTraceSummary
{
    public required int ProjectCount { get; init; }
    public required int SucceededProjects { get; init; }
    public required int FailedProjects { get; init; }
    public required int CompileInputs { get; init; }
    public required int CompiledInputs { get; init; }
    public required int ReusedCompileInputs { get; init; }
    public required int ExecutedLinks { get; init; }
    public required int ReusedLinks { get; init; }
    public required double ReusePercent { get; init; }
}

public sealed record BuildTraceProjectRecord
{
    public required string Name { get; init; }
    public required string ProjectFile { get; init; }
    public required double DurationMs { get; init; }
    public required bool Success { get; init; }
    public required int CompileInputs { get; init; }
    public required int CompiledInputs { get; init; }
    public required int ReusedCompileInputs { get; init; }
    public required string Link { get; init; }
}

[JsonSerializable(typeof(BuildTraceDocument))]
internal sealed partial class BuildTraceJsonContext : JsonSerializerContext { }

public sealed class BuildTraceSession(string target, string configuration)
{
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly ConcurrentBag<BuildTraceProjectRecord> _projects = [];

    public BuildTraceProject BeginProject(string name, string projectFile) =>
        new(this, name, projectFile);

    internal void Add(BuildTraceProjectRecord project) => _projects.Add(project);

    public BuildTraceDocument Finish()
    {
        _stopwatch.Stop();
        var projects = _projects.OrderBy(project => project.ProjectFile,
            StringComparer.OrdinalIgnoreCase).ToList();
        var compileInputs = projects.Sum(project => project.CompileInputs);
        var reusedCompiles = projects.Sum(project => project.ReusedCompileInputs);
        var executedLinks = projects.Count(project => project.Link == "executed");
        var reusedLinks = projects.Count(project => project.Link == "reused");
        var cacheableActions = compileInputs + executedLinks + reusedLinks;
        var reusedActions = reusedCompiles + reusedLinks;
        return new BuildTraceDocument
        {
            Target = target,
            Configuration = configuration,
            StartedUtc = _startedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            DurationMs = _stopwatch.Elapsed.TotalMilliseconds,
            Projects = projects,
            Summary = new BuildTraceSummary
            {
                ProjectCount = projects.Count,
                SucceededProjects = projects.Count(project => project.Success),
                FailedProjects = projects.Count(project => !project.Success),
                CompileInputs = compileInputs,
                CompiledInputs = projects.Sum(project => project.CompiledInputs),
                ReusedCompileInputs = reusedCompiles,
                ExecutedLinks = executedLinks,
                ReusedLinks = reusedLinks,
                ReusePercent = cacheableActions == 0
                    ? 100
                    : Math.Round(reusedActions * 100d / cacheableActions, 2),
            },
        };
    }

    public static void WriteJson(BuildTraceDocument document, string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(
            document, BuildTraceJsonContext.Default.BuildTraceDocument);
        File.WriteAllText(fullPath, json + Environment.NewLine);
    }
}

public sealed class BuildTraceProject
{
    private readonly BuildTraceSession _session;
    private readonly string _name;
    private readonly string _projectFile;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _compileInputs;
    private int _compiledInputs;
    private string _link = "none";

    internal BuildTraceProject(BuildTraceSession session, string name, string projectFile)
    {
        _session = session;
        _name = name;
        _projectFile = projectFile;
    }

    public void RecordCompiles(int inputs, int compiled)
    {
        _compileInputs = Math.Max(0, inputs);
        _compiledInputs = Math.Clamp(compiled, 0, _compileInputs);
    }

    public void RecordLink(bool executed) => _link = executed ? "executed" : "reused";

    public void Complete(bool success)
    {
        _stopwatch.Stop();
        _session.Add(new BuildTraceProjectRecord
        {
            Name = _name,
            ProjectFile = _projectFile,
            DurationMs = _stopwatch.Elapsed.TotalMilliseconds,
            Success = success,
            CompileInputs = _compileInputs,
            CompiledInputs = _compiledInputs,
            ReusedCompileInputs = _compileInputs - _compiledInputs,
            Link = _link,
        });
    }
}
