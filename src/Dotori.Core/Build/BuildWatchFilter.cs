namespace Dotori.Core.Build;

public static class BuildWatchFilter
{
    private static readonly HashSet<string> IgnoredDirectories = new(
        [".git", DotoriConstants.CacheDir, "bin", "obj", "build", "out", "node_modules"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IgnoredExtensions = new(
        [".o", ".obj", ".a", ".lib", ".so", ".dylib", ".dll", ".exe", ".pdb", ".d", ".pcm"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IgnoredGeneratedFiles = new(
        ["compile_commands.json", "CMakeLists.txt", "CMakePresets.json"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IdeSourceExtensions = new(
        [".c", ".cc", ".cpp", ".cxx", ".c++", ".h", ".hh", ".hpp", ".hxx", ".h++", ".cppm", ".ixx", ".mpp"],
        StringComparer.OrdinalIgnoreCase);

    public static bool ShouldTrigger(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => IgnoredDirectories.Contains(segment)))
            return false;

        var fileName = Path.GetFileName(fullPath);
        if (IgnoredGeneratedFiles.Contains(fileName)) return false;
        if (fileName.StartsWith(".", StringComparison.Ordinal) && fileName != ".dotori")
            return false;
        if (fileName.EndsWith("~", StringComparison.Ordinal) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".swp", StringComparison.OrdinalIgnoreCase))
            return false;
        return !IgnoredExtensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>Whether a file event can change clangd's compilation database.</summary>
    public static bool ShouldTriggerClangd(string path, WatcherChangeTypes changeType)
    {
        if (!ShouldTrigger(path)) return false;

        var fileName = Path.GetFileName(path);
        if (fileName.Equals(".dotori", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".dotori", StringComparison.OrdinalIgnoreCase))
            return true;

        return IdeSourceExtensions.Contains(Path.GetExtension(fileName)) &&
               changeType != WatcherChangeTypes.Changed;
    }
}
