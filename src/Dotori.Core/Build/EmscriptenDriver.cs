using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

/// <summary>
/// Generates compile/link jobs for the Emscripten toolchain (emcc).
/// </summary>
public static class EmscriptenDriver
{
    public static IReadOnlyList<string> CompileFlags(
        FlatProjectModel model,
        string objDir)
    {
        var flags = new List<string>();

        // C++ standard
        flags.Add(ClangFamilyDriver.CxxStdFlag(model.Std));

        // Optimization
        flags.Add(ClangFamilyDriver.OptimizeFlag(model.Optimize));

        if (model.Lto) flags.Add("-flto");

        flags.Add(ClangFamilyDriver.DebugInfoFlag(model.DebugInfo));

        // Defines and include directories
        ClangFamilyDriver.AddDefinesAndIncludes(flags, model);

        flags.Add("-c");

        // User-defined compile flags (appended after dotori-generated flags)
        flags.AddRange(model.CompileFlags);

        return flags.Where(f => !string.IsNullOrEmpty(f)).ToList();
    }

    public static IReadOnlyList<string> LinkFlags(
        FlatProjectModel model,
        string outputFile)
    {
        var flags = new List<string>();

        flags.Add($"-o \"{outputFile}\"");

        if (model.Lto) flags.Add("-flto");

        // Emscripten-specific flags
        foreach (var ef in model.EmscriptenFlags)
            flags.Add(ef);

        foreach (var lib in model.Links)
            flags.Add($"-l{lib}");

        // User-defined link flags (appended after dotori-generated flags)
        flags.AddRange(model.LinkFlags);

        return flags;
    }

    public static CompileJob MakeCompileJob(
        string sourceFile,
        string objDir,
        IReadOnlyList<string> commonFlags,
        bool cAsCpp = false)
    {
        var objFile = BuildOutputPath.ForSource(sourceFile, objDir, ".o");

        var args = new List<string>(commonFlags);
        if (cAsCpp && sourceFile.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            args.Add("-x c++");
        args.Add($"\"{sourceFile}\"");
        args.Add($"-o \"{objFile}\"");

        return new CompileJob
        {
            SourceFile = sourceFile,
            OutputFile = objFile,
            Args       = args.ToArray(),
        };
    }

    public static LinkJob MakeLinkJob(
        IEnumerable<string>   objFiles,
        string                outputFile,
        IReadOnlyList<string> linkFlags) =>
        LinkJobFactory.Create(objFiles, outputFile, linkFlags);
}
