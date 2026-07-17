using System.Runtime.InteropServices;
using Dotori.Core;

namespace Dotori.Core.Toolchain;

/// <summary>
/// Detects available toolchains for supported target platforms.
/// </summary>
public static partial class ToolchainDetector
{
    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Detect the best toolchain for the given target ID.
    /// </summary>
    /// <param name="targetId">
    /// One of: windows-x64, windows-x86, windows-arm64,
    ///         uwp-x64, uwp-arm64,
    ///         linux-x64, linux-arm64,
    ///         android-arm64, android-x64, android-arm,
    ///         macos-arm64, macos-x64,
    ///         ios-arm64, ios-sim-arm64, tvos-arm64, watchos-arm64_32,
    ///         wasm32-emscripten, wasm32-bare
    /// </param>
    /// <param name="preferredCompiler">
    /// Optional: "msvc" | "clang" to prefer a compiler kind, or a path/name to
    /// an explicit compiler binary (e.g. "/usr/local/bin/clang++-18", "clang++").
    /// Takes precedence over the CXX / CC environment variables.
    /// </param>
    /// <returns>Detected toolchain info, or null if not found.</returns>
    public static ToolchainInfo? Detect(
        string targetId, string? preferredCompiler = null, string? libc = null)
    {
        // Resolve an explicit compiler path from --compiler <path/name> or CXX/CC env vars.
        // CLI flag takes precedence; env vars are the fallback.
        string? compilerPathOverride = null;
        string? kindPreference       = null;

        if (preferredCompiler is not null)
        {
            if (IsKnownKindName(preferredCompiler))
                kindPreference = preferredCompiler;       // "msvc" | "clang"
            else
                compilerPathOverride = ResolveAsCompilerPath(preferredCompiler); // path or bare name
        }

        // Fall back to CXX / CC environment variables when no CLI override was given.
        compilerPathOverride ??= GetEnvCompilerPath();

        var toolchain = targetId.ToLowerInvariant() switch
        {
            "windows-x64"       => DetectWindows("x64",   kindPreference),
            "windows-x86"       => DetectWindows("x86",   kindPreference),
            "windows-arm64"     => DetectWindows("arm64", kindPreference),
            "uwp-x64"           => DetectUwp("x64"),
            "uwp-arm64"         => DetectUwp("arm64"),
            "linux-x64"         => DetectLinux("x86_64", libc),
            "linux-arm64"       => DetectLinux("aarch64", libc),
            "android-arm64"     => DetectAndroid("aarch64-linux-android21"),
            "android-x64"       => DetectAndroid("x86_64-linux-android21"),
            "android-arm"       => DetectAndroid("armv7a-linux-androideabi16"),
            "macos-arm64"       => DetectMacos("arm64-apple-macosx",  kindPreference),
            "macos-x64"         => DetectMacos("x86_64-apple-macosx", kindPreference),
            "ios-arm64"         => DetectApple("iphoneos",       "arm64-apple-ios"),
            "ios-sim-arm64"     => DetectApple("iphonesimulator","arm64-apple-ios-simulator"),
            "tvos-arm64"        => DetectApple("appletvos",      "arm64-apple-tvos"),
            "watchos-arm64_32"  => DetectApple("watchos",        "arm64_32-apple-watchos"),
            "wasm32-emscripten" => DetectEmscripten(),
            "wasm32-bare"       => DetectWasmBare(),
            _                   => null,
        };

        // Apply explicit compiler path override, keeping all platform-specific
        // metadata (sysroot, SDK paths, Apple SDK, …) from the detected toolchain.
        if (toolchain is not null && compilerPathOverride is not null)
            toolchain = ApplyCompilerOverride(toolchain, compilerPathOverride);

        return toolchain;
    }

    /// <summary>
    /// Return all target IDs for which a toolchain can be detected on this host.
    /// </summary>
    public static IReadOnlyList<string> DetectAvailable()
    {
        var all = new[]
        {
            "windows-x64", "windows-x86", "windows-arm64",
            "uwp-x64", "uwp-arm64",
            "linux-x64", "linux-arm64",
            "android-arm64", "android-x64", "android-arm",
            "macos-arm64", "macos-x64",
            "ios-arm64", "ios-sim-arm64", "tvos-arm64", "watchos-arm64_32",
            "wasm32-emscripten", "wasm32-bare",
        };
        return all.Where(t => Detect(t) is not null).ToList();
    }
}
