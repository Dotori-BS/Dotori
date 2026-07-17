namespace Dotori.Core.Build;

/// <summary>A single file compile job.</summary>
public sealed class CompileJob
{
    public required string SourceFile  { get; init; }
    public required string OutputFile  { get; init; }
    public required string[] Args      { get; init; }
    public IReadOnlyList<string> DependencyFiles { get; set; } = [];
}

/// <summary>A link job (produces an executable or library).</summary>
public sealed class LinkJob
{
    public required string[] InputFiles  { get; init; }  // .obj / .o files
    public required string   OutputFile  { get; init; }
    public required string[] Args        { get; init; }
}

/// <summary>
/// Shared factory for link jobs. All linker/driver implementations delegate here
/// to avoid duplicating the same obj-file quoting logic.
/// </summary>
internal static class LinkJobFactory
{
    /// <summary>
    /// Create a <see cref="LinkJob"/> by appending quoted object-file paths to
    /// the caller-supplied <paramref name="linkFlags"/> list.
    /// </summary>
    public static LinkJob Create(
        IEnumerable<string>   objFiles,
        string                outputFile,
        IReadOnlyList<string> linkFlags)
    {
        var inputArr = objFiles.ToArray();
        var args     = new List<string>(linkFlags);
        foreach (var obj in inputArr) args.Add($"\"{obj}\"");
        return new LinkJob
        {
            InputFiles = inputArr,
            OutputFile = outputFile,
            Args       = args.ToArray(),
        };
    }
}
