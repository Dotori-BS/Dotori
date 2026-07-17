using System.Runtime.InteropServices;

namespace Dotori.Core.Toolchain;

public static partial class ToolchainDetector
{
    // ─── Linux ───────────────────────────────────────────────────────────────

    private static ToolchainInfo? DetectLinux(string archPrefix, string? libc)
    {
        var arch = archPrefix == "x86_64" ? "x86_64" : "aarch64";

        // musl requires a compiler driver with a matching sysroot on every host.
        // A native clang installation generally only has the glibc sysroot.
        if (libc?.Equals("musl", StringComparison.OrdinalIgnoreCase) == true)
        {
            var muslCompiler = FindInPath($"{arch}-linux-musl-g++");
            if (muslCompiler is null) return null;

            var sysrootCandidate = Path.Combine(
                Path.GetDirectoryName(muslCompiler)!, "..", $"{arch}-linux-musl");
            return new ToolchainInfo
            {
                Kind         = CompilerKind.Clang,
                CompilerPath = muslCompiler,
                LinkerPath   = muslCompiler,
                TargetTriple = $"{arch}-linux-musl",
                SysRoot      = Directory.Exists(sysrootCandidate)
                    ? Path.GetFullPath(sysrootCandidate)
                    : null,
                Assembler    = DetectAssemblers(),
            };
        }

        // On Linux, use the native clang.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var clang = FindInPath("clang++") ?? FindBrewLlvm("clang++");
            if (clang is null) return null;

            return new ToolchainInfo
            {
                Kind         = CompilerKind.Clang,
                CompilerPath = clang,
                // LldLinker produces compiler-driver flags, so invoke clang++
                // and let -fuse-ld=lld select the linker implementation.
                LinkerPath   = clang,
                TargetTriple = archPrefix == "x86_64"
                    ? "x86_64-unknown-linux-gnu"
                    : "aarch64-unknown-linux-gnu",
                Assembler    = DetectAssemblers(),
            };
        }

        // On macOS, support cross-compilation via:
        // 1. musl-cross (e.g. x86_64-linux-musl-g++) — provides its own sysroot
        // 2. Homebrew LLVM + ld.lld — clang can cross-compile, lld can link Linux ELF
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectLinuxCrossOnMacos(archPrefix);

        return null;
    }

    private static ToolchainInfo? DetectLinuxCrossOnMacos(string archPrefix)
    {
        var arch = archPrefix == "x86_64" ? "x86_64" : "aarch64";

        // Prefer musl-cross toolchain (self-contained sysroot)
        var muslCompiler = FindInPath($"{arch}-linux-musl-g++");
        if (muslCompiler is not null)
        {
            // musl-cross toolchain ships its sysroot alongside the binary
            var sysrootCandidate = Path.Combine(
                Path.GetDirectoryName(muslCompiler)!, "..", $"{arch}-linux-musl");
            var sysroot = Directory.Exists(sysrootCandidate)
                ? Path.GetFullPath(sysrootCandidate)
                : null;
            return new ToolchainInfo
            {
                Kind         = CompilerKind.Clang,
                CompilerPath = muslCompiler,
                LinkerPath   = muslCompiler,
                TargetTriple = $"{arch}-linux-musl",
                SysRoot      = sysroot,
                Assembler    = DetectAssemblers(),
            };
        }

        // Fall back to Homebrew LLVM + ld.lld for cross-compilation
        // (requires a sysroot to link against glibc/musl)
        var brewClang = FindBrewLlvm("clang++");
        var lld       = FindInPath("ld.lld");
        if (brewClang is null || lld is null) return null;

        return new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = brewClang,
            LinkerPath   = brewClang,
            TargetTriple = archPrefix == "x86_64"
                ? "x86_64-unknown-linux-gnu"
                : "aarch64-unknown-linux-gnu",
            Assembler    = DetectAssemblers(),
        };
    }
}
