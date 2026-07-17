using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Dotori.Core;

namespace Dotori.PackageManager;

/// <summary>
/// Downloads npm tarballs and extracts them into the project's deps/ directory.
/// Install location: &lt;depsRoot&gt;/&lt;scopedName&gt;/ (e.g. deps/@owner/name/).
/// npm tarballs have all files under a "package/" root prefix which is stripped on extraction.
/// </summary>
public static class PackageInstaller
{
    /// <summary>Convert scoped name to a local path segment. "@owner/name" → "owner/name".</summary>
    public static string ScopedNameToPath(string scopedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopedName);
        var normalized = scopedName.TrimStart('@');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || Path.IsPathRooted(s)))
            throw new ArgumentException($"Invalid package name '{scopedName}'.", nameof(scopedName));

        return string.Join('/', segments);
    }

    /// <summary>Absolute path to the local dep directory for the given scoped name.</summary>
    public static string GetLocalDepDir(string depsRoot, string scopedName) =>
        Path.Combine(depsRoot, ScopedNameToPath(scopedName).Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Returns true if the package is already installed (package.json present).</summary>
    public static bool IsInstalled(string depsRoot, string scopedName)
    {
        var dir = GetLocalDepDir(depsRoot, scopedName);
        return Directory.Exists(dir)
            && File.Exists(Path.Combine(dir, DotoriConstants.PackageJsonFileName));
    }

    /// <summary>Download and extract a package tarball from the given URL.</summary>
    public static async Task<string> InstallFromTarballAsync(
        string depsRoot,
        string scopedName,
        string version,
        string tarballUrl,
        string? token,
        string? integrity = null,
        CancellationToken ct = default)
    {
        var depDir = GetLocalDepDir(depsRoot, scopedName);
        if (IsInstalled(depsRoot, scopedName))
            return depDir;

        var stagingRoot = Path.Combine(depsRoot, ".staging");
        var stagingDir = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dotori/1.0");
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var tmpFile = Path.Combine(stagingDir, $"{version}.tgz");
        try
        {
            using var response = await http.GetAsync(
                tarballUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write,
                             FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await stream.CopyToAsync(fs, ct);
            }
            await VerifyIntegrityAsync(tmpFile, integrity, ct);
            var packageDir = Path.Combine(stagingDir, "package");
            Directory.CreateDirectory(packageDir);
            await ExtractNpmTarballAsync(tmpFile, packageDir, ct);

            if (Directory.Exists(depDir))
                Directory.Delete(depDir, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(depDir)!);
            Directory.Move(packageDir, depDir);
        }
        finally
        {
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        }

        return depDir;
    }

    private static async Task ExtractNpmTarballAsync(
        string archivePath, string targetDir, CancellationToken ct)
    {
        await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 81920, useAsync: true);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        const string npmPrefix = "package/";

        while (await tar.GetNextEntryAsync(false, ct) is { } entry)
        {
            var name = entry.Name;

            // Strip npm's "package/" root prefix
            if (name.StartsWith(npmPrefix, StringComparison.Ordinal))
                name = name[npmPrefix.Length..];
            else
                name = name.TrimStart('.', '/');

            if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name)) continue;
            var segments = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s is "." or "..")) continue;

            var destPath = Path.GetFullPath(Path.Combine(targetDir, name));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir))
                + Path.DirectorySeparatorChar;
            if (!destPath.StartsWith(root, StringComparison.Ordinal)) continue;

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            if (entry.EntryType != TarEntryType.RegularFile) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true);
            if (entry.DataStream is not null)
                await entry.DataStream.CopyToAsync(dest, ct);
        }
    }

    /// <summary>Pack a directory into an npm-compatible .tgz (files under "package/" root).</summary>
    public static async Task<string> PackAsync(
        string projectDir,
        string outputPath,
        IEnumerable<string> filePaths,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 81920, useAsync: true);
        await using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        await using var tar = new TarWriter(gz, TarEntryFormat.Gnu);

        foreach (var filePath in filePaths)
        {
            var relative = Path.GetRelativePath(projectDir, filePath).Replace('\\', '/');
            if (relative.StartsWith("..")) continue;

            // npm standard: all files under "package/" prefix
            var entry = new GnuTarEntry(TarEntryType.RegularFile, $"package/{relative}");
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 81920, useAsync: true);
            entry.DataStream = fileStream;
            await tar.WriteEntryAsync(entry, ct);
        }

        return outputPath;
    }

    public static async Task<string> ComputeHashAsync(string archivePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Validates an npm Subresource Integrity value against an archive.</summary>
    public static async Task VerifyIntegrityAsync(
        string archivePath, string? integrity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(integrity)) return;

        foreach (var token in integrity.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('-');
            if (separator <= 0 || separator == token.Length - 1) continue;

            HashAlgorithmName algorithm = token[..separator] switch
            {
                "sha512" => HashAlgorithmName.SHA512,
                "sha384" => HashAlgorithmName.SHA384,
                "sha256" => HashAlgorithmName.SHA256,
                _ => default,
            };
            if (algorithm == default) continue;

            byte[] expected;
            try { expected = Convert.FromBase64String(token[(separator + 1)..]); }
            catch (FormatException) { continue; }

            await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 81920, useAsync: true);
            var actual = await ComputeHashAsync(fs, algorithm, ct);
            if (CryptographicOperations.FixedTimeEquals(actual, expected)) return;
        }

        throw new PackageManagerException("Downloaded package does not match its integrity hash.");
    }

    private static async Task<byte[]> ComputeHashAsync(
        Stream stream, HashAlgorithmName algorithm, CancellationToken ct) => algorithm.Name switch
        {
            "SHA512" => await SHA512.HashDataAsync(stream, ct),
            "SHA384" => await SHA384.HashDataAsync(stream, ct),
            "SHA256" => await SHA256.HashDataAsync(stream, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
}
