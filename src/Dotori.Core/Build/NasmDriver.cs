using Dotori.Core.Model;

namespace Dotori.Core.Build;

/// <summary>
/// Generates compile jobs for NASM and YASM assemblers.
/// Both tools share identical command-line interfaces (-f, -D, -i, -o).
/// </summary>
public static class NasmDriver
{
    public static CompileJob MakeJob(
        string absPath,
        string cacheDir,
        string? format,
        AssemblerConfig config)
    {
        var objFile = BuildOutputPath.ForSource(absPath, cacheDir, ".o");

        var args = new List<string>();

        if (format is not null)
            args.Add($"-f {format}");

        foreach (var d in config.Defines)
            args.Add($"-D{d}");

        args.AddRange(config.Flags);
        args.Add($"-o \"{objFile}\"");
        args.Add($"\"{absPath}\"");

        return new CompileJob
        {
            SourceFile = absPath,
            OutputFile = objFile,
            Args       = args.ToArray(),
        };
    }
}
