using Dotori.Core.Build;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Linker;

/// <summary>
/// Produces link jobs for the MSVC linker (link.exe / lib.exe).
/// Separated from MsvcDriver so the driver handles compilation flags only.
/// </summary>
public static class MsvcLinker
{
    /// <summary>
    /// Build linker flags for the given project model.
    /// </summary>
    public static IReadOnlyList<string> LinkFlags(
        FlatProjectModel model,
        ToolchainInfo    toolchain,
        string           config,
        string           outputFile)
    {
        var flags = new List<string>();

        flags.Add("/nologo");
        flags.Add($"/OUT:\"{outputFile}\"");

        // Static libraries are archives produced by lib.exe, not link.exe.
        // Keep this branch limited to librarian options; link-only flags such as
        // /DEBUG, /DLL and /LIBPATH are not meaningful while creating an archive.
        if (model.Type == ProjectType.StaticLibrary)
        {
            if (model.Lto) flags.Add("/LTCG");
            flags.AddRange(model.LinkFlags);
            return flags;
        }

        // LTO needs /LTCG
        if (model.Lto) flags.Add("/LTCG");

        // Debug info: generate .pdb
        if (model.DebugInfo != DebugInfoLevel.None)
        {
            var pdb = Path.ChangeExtension(outputFile, ".pdb");
            flags.Add($"/PDB:\"{pdb}\"");
            flags.Add("/DEBUG");
        }

        // Shared library
        if (model.Type == ProjectType.SharedLibrary)
            flags.Add("/DLL");

        // Link libraries
        foreach (var lib in model.Links)
            flags.Add($"\"{lib}.lib\"");

        // MSVC SDK lib paths
        if (toolchain.Msvc is { } msvc)
        {
            var arch = msvc.Architecture;
            // VC runtime libs (libcmt.lib, msvcrt.lib, etc.)
            flags.Add($"/LIBPATH:\"{Path.Combine(msvc.VcToolsDir, "lib", arch)}\"");
            // Windows SDK libs
            if (!string.IsNullOrEmpty(msvc.WinSdkDir))
            {
                var sdkLib = Path.Combine(msvc.WinSdkDir, "Lib", msvc.WinSdkVer);
                flags.Add($"/LIBPATH:\"{Path.Combine(sdkLib, "um",   arch)}\"");
                flags.Add($"/LIBPATH:\"{Path.Combine(sdkLib, "ucrt", arch)}\"");
            }
        }

        // User-defined link flags (appended after dotori-generated flags)
        flags.AddRange(model.LinkFlags);

        return flags;
    }

    /// <summary>Generate a link job using link.exe or lib.exe for static libraries.</summary>
    public static LinkJob MakeLinkJob(
        IEnumerable<string>   objFiles,
        string                outputFile,
        IReadOnlyList<string> linkFlags) =>
        LinkJobFactory.Create(objFiles, outputFile, linkFlags);
}
