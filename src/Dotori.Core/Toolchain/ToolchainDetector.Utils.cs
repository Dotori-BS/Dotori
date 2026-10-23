using System.Runtime.InteropServices;
using Dotori.Core;

namespace Dotori.Core.Toolchain;

public static partial class ToolchainDetector
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns true for the two reserved kind-preference names.</summary>
    internal static bool IsKnownKindName(string value) =>
        value.Equals("msvc",        StringComparison.OrdinalIgnoreCase) ||
        value.Equals("msvc-2022",   StringComparison.OrdinalIgnoreCase) ||
        value.Equals("msvc-2019",   StringComparison.OrdinalIgnoreCase) ||
        value.Equals("msvc-2017",   StringComparison.OrdinalIgnoreCase) ||
        value.Equals("clang",       StringComparison.OrdinalIgnoreCase) ||
        value.Equals("apple-clang", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("brew-llvm",   StringComparison.OrdinalIgnoreCase) ||
        value.Equals("mise-llvm",   StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the CXX environment variable (then CC as fallback) and returns the
    /// compiler path if the file exists, or null if neither variable points to a
    /// valid executable.
    /// </summary>
    internal static string? GetEnvCompilerPath()
    {
        var cxx = Environment.GetEnvironmentVariable(DotoriConstants.EnvCxx);
        if (cxx is not null)
        {
            var resolved = ResolveAsCompilerPath(cxx);
            if (resolved is not null) return resolved;
        }

        var cc = Environment.GetEnvironmentVariable(DotoriConstants.EnvCc);
        if (cc is not null)
        {
            var resolved = ResolveAsCompilerPath(cc);
            if (resolved is not null) return resolved;
        }

        return null;
    }

    /// <summary>
    /// Resolves a compiler value to an absolute path:
    /// <list type="bullet">
    ///   <item>Absolute path → checked directly.</item>
    ///   <item>Relative path (contains separator or starts with ./) → resolved via CWD.</item>
    ///   <item>Bare name (e.g. "clang++") → searched in PATH.</item>
    /// </list>
    /// Returns null when the executable cannot be found.
    /// </summary>
    internal static string? ResolveAsCompilerPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (Path.IsPathRooted(value))
            return File.Exists(value) ? value : null;

        if (value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            var full = Path.GetFullPath(value);
            return File.Exists(full) ? full : null;
        }

        // Bare name — search PATH (and add .exe on Windows).
        return FindInPath(value);
    }

    /// <summary>
    /// Infers <see cref="CompilerKind"/> from the compiler binary file name.
    /// Handles both Unix ('/') and Windows ('\') path separators regardless of host OS.
    /// </summary>
    internal static CompilerKind GuessKindFromPath(string compilerPath)
    {
        // Split on both separators so Windows paths work correctly on Unix hosts.
        var parts    = compilerPath.Split('/', '\\');
        var fileName = parts.LastOrDefault(p => p.Length > 0) ?? compilerPath;

        var dotIdx = fileName.LastIndexOf('.');
        var name   = (dotIdx >= 0 ? fileName[..dotIdx] : fileName)
                         .ToLowerInvariant();

        if (name is "cl" || name.StartsWith("clang-cl"))
            return CompilerKind.Msvc;

        if (name.StartsWith("emcc") || name.StartsWith("em++"))
            return CompilerKind.Emscripten;

        return CompilerKind.Clang;
    }

    /// <summary>
    /// Returns a new <see cref="ToolchainInfo"/> identical to <paramref name="original"/>
    /// with compiler metadata derived from <paramref name="compilerPath"/> and a
    /// linker driver compatible with that compiler.
    /// All platform-specific metadata (sysroot, SDK paths, Apple SDK, MSVC paths)
    /// are preserved so that the rest of the build pipeline still works.
    /// </summary>
    internal static ToolchainInfo ApplyCompilerOverride(
        ToolchainInfo original, string compilerPath)
    {
        var kind = GuessKindFromPath(compilerPath);
        // Clang-family drivers also understand their generated link flags and must
        // own the link step. MSVC-compatible drivers use the detected linker when
        // one is available; clang-cl can act as its own linker otherwise.
        var linkerPath = kind == CompilerKind.Msvc && original.Kind == CompilerKind.Msvc
            ? original.LinkerPath
            : compilerPath;

        return new ToolchainInfo
        {
            Kind         = kind,
            CompilerPath = compilerPath,
            LinkerPath   = linkerPath,
            TargetTriple = original.TargetTriple,
            SysRoot      = original.SysRoot,
            Msvc         = original.Msvc,
            AppleSdk     = original.AppleSdk,
            Assembler    = original.Assembler,
        };
    }

    internal static AssemblerPaths DetectAssemblers(string? masmPath = null) => new()
    {
        NasmPath = FindInPath("nasm"),
        YasmPath = FindInPath("yasm"),
        GasPath  = FindInPath("as"),
        MasmPath = masmPath,
    };

    private static string HostArch =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64  => "x64",
            Architecture.X86  => "x86",
            Architecture.Arm64 => "arm64",
            _                 => "x64",
        };

    /// <summary>
    /// Look up a Windows SDK tool (e.g. rc.exe, mt.exe) in the host-arch bin directory.
    /// Returns null if the file does not exist.
    /// </summary>
    private static string? FindSdkTool(string winSdkDir, string winSdkVer, string toolName)
    {
        var path = Path.Combine(winSdkDir, "bin", winSdkVer, HostArch, toolName);
        return File.Exists(path) ? path : null;
    }

    private static string? RunProcess(string exe, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Look for a tool in mise-managed LLVM installation (macOS/Linux).
    /// Uses MISE_DATA_DIR env or falls back to ~/.local/share/mise.
    /// Picks the lexicographically latest installed version.
    /// </summary>
    private static string? FindMiseLlvm(string toolName)
    {
        var miseHome = Environment.GetEnvironmentVariable("MISE_DATA_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "mise");
        var installsDir = Path.Combine(miseHome, "installs", "llvm");
        if (!Directory.Exists(installsDir)) return null;

        var latest = Directory.GetDirectories(installsDir)
            .OrderByDescending(d => d)
            .FirstOrDefault();
        if (latest is null) return null;

        var candidate = Path.Combine(latest, "bin", toolName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Look for a tool in common Homebrew LLVM installation paths (macOS only).
    /// Checks Apple Silicon path first, then Intel Mac path.
    /// </summary>
    private static string? FindBrewLlvm(string toolName)
    {
        var brewPrefixes = new[]
        {
            "/opt/homebrew/opt/llvm/bin", // Apple Silicon
            "/usr/local/opt/llvm/bin",    // Intel Mac
        };

        foreach (var dir in brewPrefixes)
        {
            var candidate = Path.Combine(dir, toolName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? FindInPath(string name, string? extraDir = null)
    {
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat" }
            : new[] { string.Empty };

        var searchPaths = new List<string>();
        if (extraDir is not null) searchPaths.Add(extraDir);

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        searchPaths.AddRange(pathVar.Split(Path.PathSeparator));

        foreach (var dir in searchPaths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
