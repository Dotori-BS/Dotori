using Dotori.Core.Model;

namespace Dotori.Core.Build;

/// <summary>
/// Generates compile jobs for the GNU Assembler (as).
/// GAS infers the output format from the host/target environment;
/// no explicit -f flag is needed.
/// </summary>
public static class GasDriver
{
    public static CompileJob MakeJob(
        string absPath,
        string cacheDir,
        AssemblerConfig config)
    {
        var objFile = BuildOutputPath.ForSource(absPath, cacheDir, ".o");

        var args = new List<string>();

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
