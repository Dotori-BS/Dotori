using System.Runtime.InteropServices;
using Dotori.Core;

namespace Dotori.Core.Toolchain;

public static partial class ToolchainDetector
{
    // ─── Windows / UWP ───────────────────────────────────────────────────────

    private static ToolchainInfo? DetectWindows(string arch, string? preferred)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DetectWindowsCross(arch);

        // Extract VS version from "msvc-2022", "msvc-2019", "msvc-2017"; null = latest.
        var vsVersion = preferred?.ToLowerInvariant() switch
        {
            "msvc-2022" => "2022",
            "msvc-2019" => "2019",
            "msvc-2017" => "2017",
            _           => null,
        };

        // Try MSVC first (unless clang preferred)
        if (preferred is not "clang")
        {
            var msvc = TryFindMsvc(arch, vsVersion: vsVersion);
            if (msvc is not null) return msvc;
        }

        // Try Clang
        return TryFindClangWindows(arch);
    }

    /// <summary>
    /// Detects a Windows cross-compilation toolchain on a non-Windows host.
    /// Supports llvm-mingw (prefixed clang++) and clang++ + MinGW sysroot.
    /// </summary>
    private static ToolchainInfo? DetectWindowsCross(string arch)
    {
        var triple = arch switch
        {
            "x64"   => "x86_64-w64-mingw32",
            "arm64" => "aarch64-w64-mingw32",
            _       => null,
        };
        if (triple is null) return null;

        // 1. Try prefixed clang++ (llvm-mingw style: x86_64-w64-mingw32-clang++)
        var prefixedClang = FindInPath($"{triple}-clang++");
        if (prefixedClang is not null)
        {
            // llvm-mingw sysroot is two directories above the bin/ directory
            var sysroot = Path.GetDirectoryName(Path.GetDirectoryName(prefixedClang));
            return new ToolchainInfo
            {
                Kind         = CompilerKind.Clang,
                CompilerPath = prefixedClang,
                LinkerPath   = prefixedClang,
                TargetTriple = triple,
                SysRoot      = sysroot,
                Assembler    = DetectAssemblers(),
            };
        }

        // 2. Try generic clang++ + MinGW sysroot
        var clang = FindInPath("clang++") ?? FindBrewLlvm("clang++");
        if (clang is null) return null;

        var mingwSysroot = FindMinGWSysroot(arch);
        if (mingwSysroot is null) return null;

        return new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = clang,
            LinkerPath   = clang,
            TargetTriple = triple,
            SysRoot      = mingwSysroot,
            Assembler    = DetectAssemblers(),
        };
    }

    /// <summary>
    /// Searches for a MinGW sysroot directory for the given architecture.
    /// Search order: MINGW_SYSROOT env → Homebrew → Linux system path.
    /// </summary>
    private static string? FindMinGWSysroot(string arch)
    {
        var brewArch = arch == "x64" ? "x86_64" : "aarch64";

        // Environment variable override
        var env = Environment.GetEnvironmentVariable(DotoriConstants.EnvMinGWSysroot);
        if (env is not null && Directory.Exists(env)) return env;

        // Homebrew mingw-w64 (macOS)
        var brewPaths = new[]
        {
            $"/opt/homebrew/opt/mingw-w64/toolchain-{brewArch}",  // Apple Silicon
            $"/usr/local/opt/mingw-w64/toolchain-{brewArch}",     // Intel Mac
        };
        foreach (var p in brewPaths)
            if (Directory.Exists(p)) return p;

        // Linux system MinGW-w64
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPath = $"/usr/{brewArch}-w64-mingw32";
            if (Directory.Exists(linuxPath)) return linuxPath;
        }

        return null;
    }

    private static ToolchainInfo? DetectUwp(string arch)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
        // UWP requires MSVC
        return TryFindMsvc(arch, uwp: true);
    }

    private static ToolchainInfo? TryFindMsvc(string arch, bool uwp = false, string? vsVersion = null)
    {
        // 1. Try vswhere.exe
        var vswhereResult = RunVswhere(vsVersion);
        if (vswhereResult is null) return null;

        var vcToolsDir = FindVcToolsDir(vswhereResult, arch);
        if (vcToolsDir is null) return null;

        var clExe = Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "cl.exe");
        if (!File.Exists(clExe)) return null;

        var linkExe = Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "link.exe");

        // MASM: ml64.exe (x64) or ml.exe (x86) lives in the same bin directory as cl.exe
        var masmExe = arch == "x86"
            ? Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "ml.exe")
            : Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "ml64.exe");

        return new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = clExe,
            LinkerPath   = linkExe,
            TargetTriple = arch switch
            {
                "x64"   => "x86_64-pc-windows-msvc",
                "x86"   => "i686-pc-windows-msvc",
                "arm64" => "aarch64-pc-windows-msvc",
                _       => arch,
            },
            Msvc         = TryGetMsvcPaths(vcToolsDir, arch),
            Assembler    = DetectAssemblers(masmPath: File.Exists(masmExe) ? masmExe : null),
        };
    }

    /// <summary>
    /// Collects MSVC + Windows SDK paths for a given VcToolsDir and architecture.
    /// Used by both TryFindMsvc (cl.exe) and TryFindClangWindows (clang-cl).
    /// </summary>
    private static MsvcPaths TryGetMsvcPaths(string vcToolsDir, string arch)
    {
        var (winSdkDir, winSdkVer) = FindWindowsKits();
        var libExe = Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "lib.exe");
        return winSdkDir is not null
            ? new MsvcPaths
            {
                VcToolsDir   = vcToolsDir,
                WinSdkDir    = winSdkDir,
                WinSdkVer    = winSdkVer!,
                Architecture = arch,
                LibExePath   = libExe,
                RcPath       = FindSdkTool(winSdkDir, winSdkVer!, "rc.exe"),
                MtPath       = FindSdkTool(winSdkDir, winSdkVer!, "mt.exe"),
            }
            : new MsvcPaths
            {
                VcToolsDir   = vcToolsDir,
                WinSdkDir    = string.Empty,
                WinSdkVer    = string.Empty,
                Architecture = arch,
                LibExePath   = libExe,
            };
    }

    private static ToolchainInfo? TryFindClangWindows(string arch)
    {
        var clangCl = FindInPath("clang-cl");
        var clangPp = FindInPath("clang++");
        var clang   = clangCl ?? clangPp;
        if (clang is null) return null;

        var lld = FindInPath("lld-link") ?? FindInPath("ld.lld");

        var triple = arch switch
        {
            "x64"   => "x86_64-pc-windows-msvc",
            "x86"   => "i686-pc-windows-msvc",
            "arm64" => "aarch64-pc-windows-msvc",
            _       => arch,
        };

        // clang-cl + MSVC SDK → use Msvc driver (Kind=Msvc) with clang-cl as the compiler.
        // MsvcDriver flags (/std:c++latest, /O2, /MT …) are accepted by clang-cl.
        // lld-link accepts MSVC linker flags, so MsvcLinker works unchanged.
        if (clangCl is not null)
        {
            var vswhereResult = RunVswhere();
            if (vswhereResult is not null)
            {
                var vcToolsDir = FindVcToolsDir(vswhereResult, arch);
                if (vcToolsDir is not null)
                {
                    var clangClMasmExe = arch == "x86"
                        ? Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "ml.exe")
                        : Path.Combine(vcToolsDir, "bin", $"Host{HostArch}", arch, "ml64.exe");
                    return new ToolchainInfo
                    {
                        Kind         = CompilerKind.Msvc,
                        CompilerPath = clangCl,
                        LinkerPath   = lld ?? clangCl,
                        TargetTriple = triple,
                        Msvc         = TryGetMsvcPaths(vcToolsDir, arch),
                        Assembler    = DetectAssemblers(masmPath: File.Exists(clangClMasmExe) ? clangClMasmExe : null),
                    };
                }
            }
        }

        // No MSVC SDK available — fall back to plain Clang (no SDK headers/libs).
        return new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = clang,
            LinkerPath   = lld ?? clang,
            TargetTriple = triple,
            Assembler    = DetectAssemblers(),
        };
    }

    private static string? RunVswhere(string? vsVersion = null)
    {
        // Common vswhere.exe locations
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("PROGRAMFILES(X86)") is { } pf86
                ? Path.Combine(pf86, "Microsoft Visual Studio", "Installer", "vswhere.exe")
                : null,
            Environment.GetEnvironmentVariable("PROGRAMFILES") is { } pf
                ? Path.Combine(pf, "Microsoft Visual Studio", "Installer", "vswhere.exe")
                : null,
        };

        var vswhere = candidates.FirstOrDefault(c => c is not null && File.Exists(c))
                   ?? FindInPath("vswhere");

        if (vswhere is null) return null;

        // VS version range: 2017=15.x, 2019=16.x, 2022=17.x
        var versionArg = vsVersion switch
        {
            "2017" => " -version \"[15,16)\"",
            "2019" => " -version \"[16,17)\"",
            "2022" => " -version \"[17,18)\"",
            _      => string.Empty,
        };

        return RunProcess(vswhere,
            $"-latest{versionArg} -products * -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationPath");
    }

    private static string? FindVcToolsDir(string vsInstallPath, string arch)
    {
        var vcDir = Path.Combine(vsInstallPath.Trim(), "VC", "Tools", "MSVC");
        if (!Directory.Exists(vcDir)) return null;

        var latest = Directory.GetDirectories(vcDir)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        return latest;
    }

    private static (string? dir, string? ver) FindWindowsKits()
    {
        // Check registry or common paths
        var kitsRoots = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10",
            @"C:\Program Files\Windows Kits\10",
        };

        foreach (var root in kitsRoots)
        {
            if (!Directory.Exists(root)) continue;
            var includeDir = Path.Combine(root, "Include");
            if (!Directory.Exists(includeDir)) continue;

            var ver = Directory.GetDirectories(includeDir)
                .Select(Path.GetFileName)
                .Where(v => v is not null && v.StartsWith("10."))
                .OrderByDescending(v => v)
                .FirstOrDefault();

            if (ver is not null) return (root, ver);
        }

        return (null, null);
    }
}
