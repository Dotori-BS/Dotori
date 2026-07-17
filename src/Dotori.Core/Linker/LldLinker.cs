using Dotori.Core.Build;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Linker;

/// <summary>
/// Produces link jobs for the LLD linker (ld.lld / lld-link),
/// used with Clang on Linux, Android, and WASM targets.
/// For Windows Clang + lld-link, use the MsvcLinker with appropriate flags.
/// </summary>
public static class LldLinker
{
    /// <summary>Build linker flags for the given project model.</summary>
    public static IReadOnlyList<string> LinkFlags(
        FlatProjectModel model,
        ToolchainInfo    toolchain,
        string           outputFile)
    {
        var flags = new List<string>();

        // Embed Android API level in the triple when set (e.g. aarch64-linux-android21)
        var triple = toolchain.TargetTriple;
        if (triple.Contains("android") && model.AndroidApiLevel.HasValue)
        {
            var i = triple.Length - 1;
            while (i >= 0 && char.IsDigit(triple[i])) i--;
            triple = triple[..(i + 1)] + model.AndroidApiLevel.Value;
        }
        if (!toolchain.IsGccDriver)
            flags.Add($"--target={triple}");

        if (toolchain.SysRoot is not null)
            flags.Add($"--sysroot=\"{toolchain.SysRoot}\"");

        flags.Add($"-o \"{outputFile}\"");

        // Use LLD explicitly
        if (!toolchain.IsGccDriver)
            flags.Add("-fuse-ld=lld");

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

            // musl: fully static binary
            if (toolchain.TargetTriple.Contains("musl"))
                flags.Add("-static");
        }

        // stdlib linkage
        if (!isWasmBare && model.Stdlib.HasValue && !toolchain.IsGccDriver)
        {
            flags.Add(model.Stdlib.Value switch
            {
                StdlibType.LibCxx    => "-stdlib=libc++",
                StdlibType.LibStdCxx => "-stdlib=libstdc++",
                _                    => string.Empty,
            });
        }

        // Link libraries
        foreach (var lib in model.Links)
            flags.Add($"-l{lib}");

        // WASM bare linker flags
        if (isWasmBare)
        {
            flags.Add("-nostdlib");
            flags.Add("-Wl,--no-entry");
            flags.Add("-Wl,--export-all");
        }

        // User-defined link flags (appended after dotori-generated flags)
        flags.AddRange(model.LinkFlags);

        return flags.Where(f => !string.IsNullOrEmpty(f)).ToList();
    }

    /// <summary>Generate a link job using lld (invoked via clang++).</summary>
    public static LinkJob MakeLinkJob(
        IEnumerable<string>   objFiles,
        string                outputFile,
        IReadOnlyList<string> linkFlags) =>
        LinkJobFactory.Create(objFiles, outputFile, linkFlags);
}
