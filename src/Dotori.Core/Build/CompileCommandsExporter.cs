using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dotori.Core.Model;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

/// <summary>A single entry in a compile_commands.json file (compilation database).</summary>
public sealed class CompileCommandEntry
{
    [JsonPropertyName("directory")]
    public required string Directory { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("output")]
    public required string Output { get; init; }
}

[JsonSerializable(typeof(List<CompileCommandEntry>))]
[JsonSerializable(typeof(CompileCommandEntry))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class CompileCommandsJsonContext : JsonSerializerContext { }

/// <summary>
/// Generates compile_commands.json (compilation database) entries
/// from a project's compile jobs. This allows clangd and other language
/// servers to provide IntelliSense, Go to Definition, and refactoring.
/// </summary>
public static class CompileCommandsExporter
{
    /// <summary>
    /// Generate compile_commands.json entries for all source files in a project,
    /// including C++ module files (.cppm/.ixx) from the modules { } block.
    /// </summary>
    public static async Task<IReadOnlyList<CompileCommandEntry>> GenerateEntriesAsync(
        FlatProjectModel  model,
        ToolchainInfo     toolchain,
        string            config,
        string            targetId,
        CancellationToken ct = default)
    {
        var planner     = new BuildPlanner(model, toolchain, config, targetId);
        var compileJobs = planner.PlanCompileJobs();
        var moduleJobs  = await planner.PlanModuleJobsAsync(ct: ct);

        var entries = new List<CompileCommandEntry>(compileJobs.Count + moduleJobs.Count);

        foreach (var job in compileJobs)
        {
            entries.Add(new CompileCommandEntry
            {
                Directory = model.ProjectDir,
                Command   = $"\"{toolchain.CompilerPath}\" {string.Join(" ", job.Args)}",
                File      = job.SourceFile,
                Output    = job.OutputFile,
            });
        }

        foreach (var job in moduleJobs)
        {
            entries.Add(new CompileCommandEntry
            {
                Directory = model.ProjectDir,
                Command   = $"\"{toolchain.CompilerPath}\" {string.Join(" ", job.Args)}",
                File      = job.SourceFile,
                Output    = job.OutputFile,
            });
        }

        return entries;
    }

    /// <summary>
    /// Write a compile_commands.json file from a list of entries.
    /// </summary>
    public static async Task WriteAsync(
        string outputPath,
        IReadOnlyList<CompileCommandEntry> entries,
        CancellationToken ct = default)
    {
        // Use source-generated JsonTypeInfo for NativeAOT compatibility.
        // Apply UnsafeRelaxedJsonEscaping so paths with +, /, etc. are not escaped.
        var typeInfo = (JsonTypeInfo<List<CompileCommandEntry>>)
            CompileCommandsJsonContext.Default.GetTypeInfo(typeof(List<CompileCommandEntry>))!;

        var options = new JsonSerializerOptions(typeInfo.Options)
        {
            WriteIndented = true,
            Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var resolvedTypeInfo = (JsonTypeInfo<List<CompileCommandEntry>>)
            options.GetTypeInfo(typeof(List<CompileCommandEntry>));

        var list = entries is List<CompileCommandEntry> l ? l : new List<CompileCommandEntry>(entries);
        var json = JsonSerializer.Serialize(list, resolvedTypeInfo);
        await File.WriteAllTextAsync(outputPath, json, ct);
    }
}
