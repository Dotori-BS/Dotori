using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

public static class SwiftDriver
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> RuntimePaths =
        new(StringComparer.OrdinalIgnoreCase);

    public static string? FindCompiler()
    {
        var configured = Environment.GetEnvironmentVariable("SWIFTC");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "swiftc.exe" : "swiftc");
            if (File.Exists(candidate)) return candidate;
        }
        if (OperatingSystem.IsMacOS())
            return Run("xcrun", ["--find", "swiftc"])?.Trim() is { Length: > 0 } path
                ? path
                : null;
        return null;
    }

    public static CompileJob MakeCompileJob(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        string config,
        string cacheDir,
        IReadOnlyList<string> sources,
        string compiler,
        bool hasCxxSources,
        IReadOnlyList<string> dependencyModuleDirectories)
    {
        var moduleName = SanitizeModuleName(model.Name);
        var objectFile = Path.Combine(cacheDir, $"{moduleName}.swift.o");
        var moduleFile = Path.Combine(cacheDir, $"{moduleName}.swiftmodule");
        var moduleCache = Path.Combine(cacheDir, "SwiftModuleCache");
        Directory.CreateDirectory(moduleCache);
        var args = new List<string>
        {
            "-target", SwiftTargetTriple(model, toolchain),
            "-module-name", moduleName,
            "-parse-as-library",
            "-whole-module-optimization",
            "-emit-module", "-emit-module-path", $"\"{moduleFile}\"",
            "-module-cache-path", $"\"{moduleCache}\"",
            "-c",
        };
        if (toolchain.AppleSdk is not null)
        {
            args.Add("-sdk");
            args.Add($"\"{toolchain.AppleSdk}\"");
        }
        if (config.Equals("release", StringComparison.OrdinalIgnoreCase))
            args.Add("-O");
        else
        {
            args.Add("-Onone");
            args.Add("-g");
        }

        if (hasCxxSources)
            args.Add("-cxx-interoperability-mode=default");

        foreach (var moduleDirectory in dependencyModuleDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            args.Add("-I");
            args.Add($"\"{moduleDirectory}\"");
        }
        foreach (var source in sources) args.Add($"\"{source}\"");
        args.Add("-o");
        args.Add($"\"{objectFile}\"");

        var job = new CompileJob
        {
            SourceFile = sources[0],
            OutputFile = objectFile,
            Args = args.ToArray(),
            DependencyFiles = sources.Skip(1).ToArray(),
        };
        job.Fingerprint = BuildActionFingerprint.Compute(compiler, job.Args, job.DependencyFiles);
        return job;
    }

    public static IReadOnlyList<string> LinkFlags(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        string outputFile,
        string compiler,
        bool hasCxxSources)
    {
        var flags = new List<string> { "-target", SwiftTargetTriple(model, toolchain) };
        if (toolchain.AppleSdk is not null)
        {
            flags.Add("-sdk");
            flags.Add($"\"{toolchain.AppleSdk}\"");
        }
        flags.Add("-o");
        flags.Add($"\"{outputFile}\"");
        if (model.Type == ProjectType.SharedLibrary) flags.Add("-emit-library");
        if (model.Lto) flags.Add("-lto=llvm-full");
        foreach (var path in GetRuntimeLibraryPaths(compiler))
        {
            flags.Add($"-L\"{path}\"");
            flags.Add("-Xlinker");
            flags.Add("-rpath");
            flags.Add("-Xlinker");
            flags.Add($"\"{path}\"");
        }
        if (hasCxxSources)
            flags.Add("-lc++");
        foreach (var path in model.FrameworkSearchPaths) flags.Add($"-F\"{path}\"");
        foreach (var framework in model.Frameworks)
        {
            flags.Add("-framework");
            flags.Add(framework);
        }
        foreach (var library in model.Links) flags.Add($"-l{library}");
        flags.AddRange(model.LinkFlags);
        return flags;
    }

    public static IReadOnlyList<string> GetRuntimeLibraryPaths(string compiler) =>
        RuntimePaths.GetOrAdd(compiler, static swiftc =>
        {
            var output = Run(swiftc, ["-print-target-info"]);
            if (string.IsNullOrWhiteSpace(output)) return [];
            try
            {
                using var json = JsonDocument.Parse(output);
                return json.RootElement.GetProperty("paths")
                    .GetProperty("runtimeLibraryPaths")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToArray();
            }
            catch (JsonException) { return []; }
        });

    public static string SanitizeModuleName(string name)
    {
        var chars = name.Select(character => char.IsLetterOrDigit(character) || character == '_'
            ? character : '_').ToArray();
        var result = new string(chars);
        return result.Length > 0 && !char.IsDigit(result[0]) ? result : $"_{result}";
    }

    private static string SwiftTargetTriple(FlatProjectModel model, ToolchainInfo toolchain)
    {
        var triple = toolchain.TargetTriple;
        if (triple.EndsWith("-apple-macosx", StringComparison.OrdinalIgnoreCase))
            return triple + (model.MacosMin ?? "10.15");
        if (triple.EndsWith("-apple-ios", StringComparison.OrdinalIgnoreCase))
            return triple + (model.IosMin ?? "13.0");
        return triple;
    }

    private static string? Run(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }
}
