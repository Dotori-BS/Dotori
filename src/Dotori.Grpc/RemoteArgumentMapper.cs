using Dotori.Core.Build;

namespace Dotori.Grpc;

/// <summary>Maps coordinator-local paths to worker-local placeholders.</summary>
public static class RemoteArgumentMapper
{
    public const string SourcePlaceholder = "{DOTORI_SOURCE}";
    public const string OutputPlaceholder = "{DOTORI_OUTPUT}";

    public static IReadOnlyList<string> MapCompile(CompileJob job) =>
        job.Args.Select(argument => ReplacePath(
            ReplacePath(argument, job.SourceFile, SourcePlaceholder),
            job.OutputFile, OutputPlaceholder)).ToArray();

    public static IReadOnlyList<string> MapLink(LinkJob job)
    {
        var result = new string[job.Args.Length];
        for (var argIndex = 0; argIndex < job.Args.Length; argIndex++)
        {
            var argument = ReplacePath(job.Args[argIndex], job.OutputFile, OutputPlaceholder);
            for (var inputIndex = 0; inputIndex < job.InputFiles.Length; inputIndex++)
                argument = ReplacePath(argument, job.InputFiles[inputIndex], InputPlaceholder(inputIndex));
            result[argIndex] = argument;
        }
        return result;
    }

    public static IReadOnlyList<string> MaterializeCompile(
        IEnumerable<string> args, string sourceFile, string outputFile) =>
        args.Select(argument => argument
            .Replace(SourcePlaceholder, sourceFile, StringComparison.Ordinal)
            .Replace(OutputPlaceholder, outputFile, StringComparison.Ordinal))
            .ToArray();

    public static IReadOnlyList<string> MaterializeLink(
        IEnumerable<string> args, IReadOnlyList<string> inputFiles, string outputFile)
    {
        return args.Select(argument =>
        {
            var mapped = argument.Replace(OutputPlaceholder, outputFile, StringComparison.Ordinal);
            for (var index = 0; index < inputFiles.Count; index++)
                mapped = mapped.Replace(InputPlaceholder(index), inputFiles[index], StringComparison.Ordinal);
            return mapped;
        }).ToArray();
    }

    private static string InputPlaceholder(int index) => $"{{DOTORI_INPUT_{index}}}";

    private static string ReplacePath(string argument, string path, string placeholder) =>
        argument.Replace(path, placeholder, StringComparison.OrdinalIgnoreCase);
}
