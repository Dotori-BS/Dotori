using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Core.Build;

public sealed partial class BuildPlanner
{
    // ─── Artifact copy ────────────────────────────────────────────────────────

    /// <summary>
    /// Copy the linked artifact(s) to user-specified output directories
    /// defined in the <c>output { }</c> block of the .dotori file.
    /// Does nothing if <see cref="FlatProjectModel.Output"/> is null.
    /// </summary>
    /// <param name="linkedFile">Absolute path to the primary linked output file.</param>
    public void CopyArtifacts(string linkedFile)
    {
        if (_model.Output is null) return;
        if (!File.Exists(linkedFile)) return;

        bool isWindows = _targetId.StartsWith("windows", StringComparison.OrdinalIgnoreCase)
                      || _targetId.StartsWith("uwp",     StringComparison.OrdinalIgnoreCase);

        // Determine which output category the linked file belongs to
        switch (_model.Type)
        {
            case ProjectType.Executable:
            case ProjectType.Test:
                CopyTo(linkedFile, _model.Output.Binaries);
                // Windows PDB alongside exe
                if (isWindows) CopySymbols(linkedFile, ".pdb");
                break;

            case ProjectType.SharedLibrary:
                CopyTo(linkedFile, _model.Output.Binaries);
                // Windows import library: same stem + .lib
                if (isWindows)
                {
                    var importLib = Path.ChangeExtension(linkedFile, ".lib");
                    CopyTo(importLib, _model.Output.Libraries);
                    CopySymbols(linkedFile, ".pdb");
                }
                // macOS .dSYM bundle
                if (_targetId.StartsWith("macos", StringComparison.OrdinalIgnoreCase))
                    CopyDsym(linkedFile);
                break;

            case ProjectType.StaticLibrary:
                CopyTo(linkedFile, _model.Output.Libraries);
                break;
        }
    }

    private void CopyTo(string sourceFile, string? destDir)
    {
        if (destDir is null || !File.Exists(sourceFile)) return;

        var absDestDir = PathUtils.MakeAbsolute(_model.ProjectDir, destDir);
        Directory.CreateDirectory(absDestDir);

        var dest = Path.Combine(absDestDir, Path.GetFileName(sourceFile));
        File.Copy(sourceFile, dest, overwrite: true);
    }

    private void CopySymbols(string linkedFile, string ext)
    {
        var symFile = Path.ChangeExtension(linkedFile, ext);
        CopyTo(symFile, _model.Output?.Symbols);
    }

    private void CopyDsym(string linkedFile)
    {
        if (_model.Output?.Symbols is null) return;
        var dsymBundle = linkedFile + ".dSYM";
        if (!Directory.Exists(dsymBundle)) return;

        var absDestDir = PathUtils.MakeAbsolute(_model.ProjectDir, _model.Output.Symbols);
        Directory.CreateDirectory(absDestDir);

        CopyDirectoryRecursive(dsymBundle, Path.Combine(absDestDir, Path.GetFileName(dsymBundle)));
    }

    private static void CopyDirectoryRecursive(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
