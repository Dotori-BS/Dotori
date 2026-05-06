using System.CommandLine;
using System.Text;
using System.Text.Json;
using Dotori.Core.Parsing;
using Dotori.PackageManager;
using Dotori.PackageManager.Config;

namespace Dotori.Cli.Commands;

internal static class PublishCommandFactory
{
    public static Command Create()
    {
        var command = new Command("publish", "Publish a package to an npm-compatible registry");

        var projectOption  = new Option<string?>("--project")  { Description = "Path to package.json or project directory" };
        var registryOption = new Option<string?>("--registry") { Description = "Registry URL" };
        var prefixOption   = new Option<string?>("--prefix")   { Description = "Local directory to deploy into (instead of registry)" };
        var dryRunOption   = new Option<bool>("--dry-run")     { Description = "Validate without uploading" };

        command.Add(projectOption);
        command.Add(registryOption);
        command.Add(prefixOption);
        command.Add(dryRunOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg = parseResult.GetValue(projectOption);
            var regUrl     = parseResult.GetValue(registryOption);
            var prefix     = parseResult.GetValue(prefixOption);
            var dryRun     = parseResult.GetValue(dryRunOption);

            if (regUrl is not null && prefix is not null)
            {
                Console.Error.WriteLine("error: --registry and --prefix are mutually exclusive");
                return;
            }

            var projectDir = FindProjectDir(projectArg);
            if (projectDir is null)
            {
                Console.Error.WriteLine("error: no package.json found");
                return;
            }

            // Load package.json
            var manifest = PackageJsonManifest.Load(projectDir);
            if (manifest is null)
            {
                Console.Error.WriteLine("error: package.json not found or invalid");
                return;
            }

            var pkgName    = manifest.Name;
            var pkgVersion = manifest.Version;

            if (string.IsNullOrEmpty(pkgName) || string.IsNullOrEmpty(pkgVersion))
            {
                Console.Error.WriteLine("error: package.json must have 'name' and 'version'");
                return;
            }

            if (!pkgName.StartsWith('@'))
            {
                Console.Error.WriteLine($"warning: package name '{pkgName}' is not scoped (@scope/name)");
            }

            Console.WriteLine($"Publishing {pkgName} v{pkgVersion}...");

            // Collect files for packing
            var filesToPack = CollectFiles(projectDir, projectDir);
            Console.WriteLine($"  Files: {filesToPack.Count}");

            // Pack into .tgz (npm format)
            var slash = pkgName.LastIndexOf('/');
            var shortName = slash >= 0 ? pkgName[(slash + 1)..] : pkgName;
            var tmpPath = Path.GetTempFileName() + ".tgz";
            try
            {
                await PackageInstaller.PackAsync(projectDir, tmpPath, filesToPack, ct);
                var hash = await PackageInstaller.ComputeHashAsync(tmpPath, ct);
                var size = new FileInfo(tmpPath).Length;

                Console.WriteLine($"  Archive: {size / 1024.0:F1} KB, {hash}");

                if (dryRun)
                {
                    Console.WriteLine("[dry-run] Skipping upload");
                    return;
                }

                if (prefix is not null)
                {
                    // Local prefix directory
                    var destDir = Path.Combine(Path.GetFullPath(prefix),
                        pkgName.TrimStart('@').Replace('/', Path.DirectorySeparatorChar),
                        pkgVersion);
                    Directory.CreateDirectory(destDir);
                    var destFile = Path.Combine(destDir, $"{shortName}-{pkgVersion}.tgz");
                    File.Copy(tmpPath, destFile, overwrite: true);
                    Console.WriteLine($"Successfully published {pkgName} v{pkgVersion} → {destFile}");
                }
                else
                {
                    // npm publish protocol: PUT /@scope/name with JSON + base64 tarball
                    var config = DotoriConfigManager.Load(projectDir);
                    var resolvedUrl = regUrl ?? config.ResolveRegistryUrl(pkgName);
                    var regConfig = config.GetRegistry(resolvedUrl);

                    if (regConfig.Token is null)
                    {
                        Console.Error.WriteLine("error: not logged in. Set token in ~/.dotori/config.toml or .npmrc");
                        return;
                    }

                    var tarballBytes = await File.ReadAllBytesAsync(tmpPath, ct);
                    var filename = $"{shortName}-{pkgVersion}.tgz";

                    var body = new NpmPublishBody
                    {
                        Id   = pkgName,
                        Name = pkgName,
                        Versions = new() {
                            [pkgVersion] = new NpmVersionManifest
                            {
                                Name        = pkgName,
                                Version     = pkgVersion,
                                Description = manifest.Description,
                                License     = manifest.License,
                                Dependencies = manifest.Dependencies,
                                DotoriDependencies = manifest.DotoriDependencies,
                                Dotori      = manifest.Dotori,
                            }
                        },
                        Attachments = new() {
                            [filename] = new NpmAttachment
                            {
                                Data   = Convert.ToBase64String(tarballBytes),
                                Length = tarballBytes.Length,
                            }
                        },
                    };

                    using var client = new NpmRegistryClient(resolvedUrl, regConfig.Token);
                    await client.PublishAsync(pkgName, body, ct);
                    Console.WriteLine($"Successfully published {pkgName} v{pkgVersion}!");
                }
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        });

        return command;
    }

    private static string? FindProjectDir(string? projectArg)
    {
        if (projectArg is not null)
        {
            if (File.Exists(projectArg) && projectArg.EndsWith("package.json"))
                return Path.GetDirectoryName(projectArg);
            if (Directory.Exists(projectArg) &&
                File.Exists(Path.Combine(projectArg, "package.json")))
                return projectArg;
            return null;
        }

        var current = Directory.GetCurrentDirectory();
        while (true)
        {
            if (File.Exists(Path.Combine(current, "package.json"))) return current;
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || parent == current) return null;
            current = parent;
        }
    }

    private static List<string> CollectFiles(string projectDir, string rootDir)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always include package.json
        var pkgJson = Path.Combine(projectDir, "package.json");
        if (File.Exists(pkgJson)) files.Add(pkgJson);

        var license = Path.Combine(projectDir, "LICENSE");
        if (File.Exists(license)) files.Add(license);

        // Include all source and header files under projectDir
        foreach (var f in Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(projectDir, f);
            if (rel.StartsWith(".dotori-cache")) continue;
            if (rel.StartsWith(".git")) continue;
            if (rel.StartsWith("deps" + Path.DirectorySeparatorChar)) continue;
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".obj" or ".lib" or ".a" or ".pdb" or ".tgz") continue;
            files.Add(f);
        }

        return files.ToList();
    }
}
