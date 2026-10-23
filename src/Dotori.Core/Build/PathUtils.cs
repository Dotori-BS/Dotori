namespace Dotori.Core.Build;

/// <summary>
/// Shared path helper utilities used by compiler drivers and the build planner.
/// </summary>
public static class PathUtils
{
    /// <summary>
    /// Returns <paramref name="path"/> as an absolute path.
    /// If the path is already rooted, it is returned as-is.
    /// Otherwise it is resolved relative to <paramref name="basePath"/>.
    /// </summary>
    public static string MakeAbsolute(string basePath, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(basePath, path));

    /// <summary>
    /// Resolves a directory path and removes trailing separators before it is
    /// embedded in a quoted compiler argument. A trailing backslash can escape
    /// the closing quote in Windows command-line parsing.
    /// </summary>
    public static string MakeAbsoluteDirectory(string basePath, string path) =>
        Path.TrimEndingDirectorySeparator(MakeAbsolute(basePath, path));

    /// <summary>Only project-owned subdirectories may be recursively cleaned.</summary>
    public static bool IsSafeCleanDirectory(string projectDir, string candidate)
    {
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDir));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (string.Equals(project, target, PathComparison)) return false;

        var prefix = project + Path.DirectorySeparatorChar;
        return target.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
