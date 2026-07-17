using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

/// <summary>
/// Generates compile/link jobs for the Clang toolchain (clang++).
/// Handles Linux, macOS, iOS, Android, and WASM bare targets.
/// </summary>
public static class ClangDriver
{
    /// <summary>
    /// Returns the effective target triple, replacing the API level suffix when
    /// <paramref name="model"/>.AndroidApiLevel overrides the toolchain default.
    /// </summary>
    private static string ResolveTriple(ToolchainInfo toolchain, FlatProjectModel model)
    {
        var triple = toolchain.TargetTriple;
        if (triple.Contains("android") && model.AndroidApiLevel.HasValue)
        {
            // Strip trailing digits (default API level) and append the requested one.
            var i = triple.Length - 1;
            while (i >= 0 && char.IsDigit(triple[i])) i--;
            triple = triple[..(i + 1)] + model.AndroidApiLevel.Value;
        }
        return triple;
    }

    /// <summary>
    /// Build the common compiler flags for a flattened project model.
    /// </summary>
    public static IReadOnlyList<string> CompileFlags(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        string config,
        string objDir)
    {
        var flags = new List<string>();

        // Target triple (respects android-api-level override)
        if (!toolchain.IsGccDriver)
            flags.Add($"--target={ResolveTriple(toolchain, model)}");

        // Sysroot
        if (toolchain.SysRoot is not null)
            flags.Add($"--sysroot=\"{toolchain.SysRoot}\"");

        // Apple SDK
        if (toolchain.AppleSdk is not null)
            flags.Add($"-isysroot \"{toolchain.AppleSdk}\"");

        // C++ standard
        flags.Add(ClangFamilyDriver.CxxStdFlag(model.Std));

        // C++ stdlib
        if (model.Stdlib.HasValue && !toolchain.IsGccDriver)
        {
            flags.Add(model.Stdlib.Value switch
            {
                StdlibType.LibCxx    => "-stdlib=libc++",
                StdlibType.LibStdCxx => "-stdlib=libstdc++",
                _                    => string.Empty,
            });
        }

        // Optimization
        flags.Add(ClangFamilyDriver.OptimizeFlag(model.Optimize));

        // LTO
        if (model.Lto) flags.Add("-flto");

        // Debug info
        flags.Add(ClangFamilyDriver.DebugInfoFlag(model.DebugInfo));

        // Warnings
        flags.Add(model.Warnings switch
        {
            WarningLevel.None    => "-w",
            WarningLevel.Default => string.Empty,
            WarningLevel.All     => "-Wall",
            WarningLevel.Extra   => "-Wall -Wextra",
            _                    => string.Empty,
        });
        if (model.WarningsAsErrors) flags.Add("-Werror");

        // Defines and include directories (resolve relative to project root)
        ClangFamilyDriver.AddDefinesAndIncludes(flags, model, model.ProjectDir);

        // Custom framework search paths (-F) from framework-paths / xcframework resolution
        foreach (var fp in model.FrameworkSearchPaths)
            flags.Add($"-F\"{fp}\"");

        // macOS minimum version
        if (model.MacosMin is not null &&
            toolchain.TargetTriple.Contains("macosx"))
            flags.Add($"-mmacosx-version-min={model.MacosMin}");

        // iOS minimum version
        if (model.IosMin is not null &&
            toolchain.TargetTriple.Contains("apple-ios"))
            flags.Add($"-miphoneos-version-min={model.IosMin}");

        // Android API level
        if (toolchain.TargetTriple.Contains("android") && model.AndroidApiLevel.HasValue)
        {
            // The API level is embedded in the triple (e.g. aarch64-linux-android26)
            // Just add as a define for consistency
            flags.Add($"-DANDROID_API_LEVEL={model.AndroidApiLevel}");
        }

        // WASM bare additional flags
        if (toolchain.TargetTriple == "wasm32-unknown-unknown")
        {
            flags.Add("--no-standard-libraries");
        }

        // Compile only (no link)
        flags.Add("-c");

        // User-defined compile flags (appended after dotori-generated flags)
        flags.AddRange(model.CompileFlags);

        // Remove any empty strings
        return flags.Where(f => !string.IsNullOrEmpty(f)).ToList();
    }

    /// <summary>Build linker flags for the given project model.</summary>
    public static IReadOnlyList<string> LinkFlags(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        string outputFile)
    {
        var flags = new List<string>();

        flags.Add($"--target={ResolveTriple(toolchain, model)}");

        if (toolchain.SysRoot is not null)
            flags.Add($"--sysroot=\"{toolchain.SysRoot}\"");

        if (toolchain.AppleSdk is not null)
            flags.Add($"-isysroot \"{toolchain.AppleSdk}\"");

        flags.Add($"-o \"{outputFile}\"");

        if (model.Lto) flags.Add("-flto");

        // Library type
        if (model.Type == ProjectType.SharedLibrary)
            flags.Add("-shared");

        // wasm32-bare has no OS stdlib — skip all runtime/stdlib link flags
        bool isWasmBare = toolchain.TargetTriple == "wasm32-unknown-unknown";

        // Runtime static link
        if (!isWasmBare && model.RuntimeLink == RuntimeLink.Static)
        {
            if (model.Stdlib == StdlibType.LibCxx)
            {
                flags.Add("-static-libstdc++");
                flags.Add("-lc++abi");
            }
            else
            {
                flags.Add("-static-libgcc");
                flags.Add("-static-libstdc++");
            }

            // musl full static
            if (toolchain.TargetTriple.Contains("musl"))
                flags.Add("-static");
        }

        // Link libraries
        foreach (var lib in model.Links)
            flags.Add($"-l{lib}");

        // Apple frameworks
        foreach (var fw in model.Frameworks)
        {
            flags.Add("-framework");
            flags.Add(fw);
        }

        // macOS minimum version
        if (model.MacosMin is not null && toolchain.TargetTriple.Contains("macosx"))
            flags.Add($"-mmacosx-version-min={model.MacosMin}");

        // WASM bare linker flags
        if (isWasmBare)
        {
            flags.Add("-Wl,--no-entry");
            flags.Add("-Wl,--export-all");
        }

        // User-defined link flags (appended after dotori-generated flags)
        flags.AddRange(model.LinkFlags);

        return flags;
    }

    private static bool IsObjcSource(string sourceFile) =>
        sourceFile.EndsWith(".m",  StringComparison.OrdinalIgnoreCase);

    private static bool IsObjcppSource(string sourceFile) =>
        sourceFile.EndsWith(".mm", StringComparison.OrdinalIgnoreCase);

    /// <summary>Generate a compile job for a single source file.</summary>
    public static CompileJob MakeCompileJob(
        string sourceFile,
        string objDir,
        IReadOnlyList<string> commonFlags,
        bool isModule = false,
        bool cAsCpp = false,
        bool objcArc = false,
        bool forceObjcpp = false)
    {
        var ext     = isModule ? ".pcm" : ".o";
        var objFile = Path.Combine(objDir,
            Path.GetFileNameWithoutExtension(sourceFile) + ext);

        var args = new List<string>(commonFlags);

        if (cAsCpp && sourceFile.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            args.Add("-x c++");
        else if (forceObjcpp && IsObjcSource(sourceFile))
            args.Add("-x objective-c++");
        else if (IsObjcppSource(sourceFile))
            args.Add("-x objective-c++");
        else if (IsObjcSource(sourceFile))
            args.Add("-x objective-c");

        if (objcArc && (IsObjcSource(sourceFile) || IsObjcppSource(sourceFile)
                        || (forceObjcpp && IsObjcSource(sourceFile))))
            args.Add("-fobjc-arc");

        args.Add($"\"{sourceFile}\"");
        if (isModule)
        {
            // Module precompilation
            args.Remove("-c");
            args.Add("--precompile");
            args.Add("-x c++-module");
        }

        args.Add($"-o \"{objFile}\"");

        return new CompileJob
        {
            SourceFile = sourceFile,
            OutputFile = objFile,
            Args       = args.ToArray(),
        };
    }

    /// <summary>Generate a link job.</summary>
    public static LinkJob MakeLinkJob(
        IEnumerable<string>   objFiles,
        string                outputFile,
        IReadOnlyList<string> linkFlags) =>
        LinkJobFactory.Create(objFiles, outputFile, linkFlags);
}
