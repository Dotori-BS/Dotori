using Dotori.Core.Model;

namespace Dotori.Core.Build;

/// <summary>
/// Generates compile jobs for the Microsoft Macro Assembler (ml64.exe / ml.exe).
/// MASM always targets the Windows ABI; no explicit format flag is required.
/// </summary>
public static class MasmDriver
{
    public static CompileJob MakeJob(
        string absPath,
        string cacheDir,
        AssemblerConfig config)
    {
        var objFile = BuildOutputPath.ForSource(absPath, cacheDir, ".obj");

        var args = new List<string> { "/nologo", "/c" };

        foreach (var d in config.Defines)
            args.Add($"/D{d}");

        args.AddRange(config.Flags);
        args.Add($"/Fo\"{objFile}\"");
        args.Add($"\"{absPath}\"");

        return new CompileJob
        {
            SourceFile = absPath,
            OutputFile = objFile,
            Args       = args.ToArray(),
        };
    }
}
