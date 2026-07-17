using System.Security.Cryptography;
using System.Text;

namespace Dotori.Core.Build;

/// <summary>Creates stable, collision-resistant output names for source files.</summary>
internal static class BuildOutputPath
{
    public static string ForSource(string sourceFile, string outputDir, string extension)
    {
        var fullPath = Path.GetFullPath(sourceFile)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
            fullPath = fullPath.ToUpperInvariant();

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        var suffix = Convert.ToHexStringLower(digest.AsSpan(0, 8));
        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        return Path.Combine(outputDir, $"{stem}.{suffix}{extension}");
    }
}
