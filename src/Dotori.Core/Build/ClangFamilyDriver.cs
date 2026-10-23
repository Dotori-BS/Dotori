using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Core.Build;

/// <summary>
/// Shared flag-building helpers for Clang-family compilers (clang++ and emcc).
/// </summary>
internal static class ClangFamilyDriver
{
    /// <summary>Maps CxxStd to its -std flag value.</summary>
    internal static string CxxStdFlag(CxxStd std) => std switch
    {
        CxxStd.Cxx17 => "-std=c++17",
        CxxStd.Cxx20 => "-std=c++20",
        _             => "-std=c++23",
    };

    /// <summary>Maps OptimizeLevel to its -O flag value.</summary>
    internal static string OptimizeFlag(OptimizeLevel level) => level switch
    {
        OptimizeLevel.None  => "-O0",
        OptimizeLevel.Size  => "-Os",
        OptimizeLevel.Speed => "-O2",
        OptimizeLevel.Full  => "-O3",
        _                   => "-O0",
    };

    /// <summary>Maps DebugInfoLevel to its -g flag value (empty string when None).</summary>
    internal static string DebugInfoFlag(DebugInfoLevel level) => level switch
    {
        DebugInfoLevel.Full    => "-g",
        DebugInfoLevel.Minimal => "-gline-tables-only",
        _                      => string.Empty,
    };

    /// <summary>
    /// Appends defines (-Dname) and include paths (-I"path") flags to <paramref name="flags"/>.
    /// </summary>
    internal static void AddDefinesAndIncludes(
        List<string> flags,
        FlatProjectModel model,
        string? projectDir = null)
    {
        foreach (var d in model.Defines)
            flags.Add($"-D{d}");

        foreach (var h in model.Headers)
        {
            var path = projectDir is not null
                ? PathUtils.MakeAbsoluteDirectory(projectDir, h.Path)
                : Path.TrimEndingDirectorySeparator(h.Path);
            flags.Add($"-I\"{path}\"");
        }
    }
}
