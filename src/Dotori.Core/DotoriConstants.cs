namespace Dotori.Core;

/// <summary>
/// Project-wide constants for directory names, file names, and default values.
/// Centralises magic strings and numbers that were previously scattered across the codebase.
/// </summary>
public static class DotoriConstants
{
    // ─── File / directory names ────────────────────────────────────────────────

    /// <summary>Extension used by all .dotori project files.</summary>
    public const string DotoriExtension = ".dotori";

    /// <summary>Legacy lock file name (v1, TOML-like). Kept for migration only.</summary>
    public const string LockFileName = ".dotori.lock";

    /// <summary>Lock file name (v2, JSON). Written next to the .dotori file.</summary>
    public const string LockJsonFileName = "dotori-lock.json";

    /// <summary>npm-compatible package manifest file name.</summary>
    public const string PackageJsonFileName = "package.json";

    /// <summary>Local override file name. Lives next to the .dotori file and is ignored by VCS.</summary>
    public const string LocalFileName = ".dotori.local";

    /// <summary>Root cache directory created inside every project directory.</summary>
    public const string CacheDir = ".dotori-cache";

    /// <summary>Local dependencies directory created inside the project directory (mirrors Elixir Mix's deps/).</summary>
    public const string DepsDir = "deps";

    /// <summary>Sub-directory under <see cref="CacheDir"/> for compiled object files.</summary>
    public const string ObjSubDir = "obj";

    /// <summary>Sub-directory under <see cref="CacheDir"/> for linked binaries (before artifact copy).</summary>
    public const string BinSubDir = "bin";

    /// <summary>Sub-directory under <see cref="CacheDir"/> for BMI (.pcm / .ifc) files.</summary>
    public const string BmiSubDir = "bmi";

    /// <summary>Sub-directory under <see cref="CacheDir"/> for unity-build amalgamation files.</summary>
    public const string UnitySubDir = "unity";

    /// <summary>Sub-directory under <see cref="CacheDir"/> for pre-compiled headers.</summary>
    public const string PchSubDir = "pch";

    /// <summary>Incremental build hash database file name inside <see cref="CacheDir"/>.</summary>
    public const string HashDbFileName = "hashes.db";

    /// <summary>Module export-map file name written inside the BMI directory.</summary>
    public const string ModuleMapFileName = "module-map.json";

    /// <summary>Copy manifest file name written inside the cache directory. Maps source → destination paths.</summary>
    public const string CopyManifestFileName = "copy-manifest.json";

    // ─── Default numeric values ────────────────────────────────────────────────

    /// <summary>Default number of source files per unity-build batch.</summary>
    public const int DefaultUnityBatchSize = 8;

    /// <summary>
    /// Specificity offset applied to all layers from the local override file (.dotori.local).
    /// Ensures every local setting — even unconditional top-level ones — outranks any conditional
    /// block in the main .dotori file. The maximum normal specificity is bounded by the number
    /// of condition atoms a user can realistically write; 10000 is safely above that.
    /// </summary>
    public const int LocalSpecificityOffset = 10000;

    /// <summary>mt.exe resource ID for executable binaries.</summary>
    public const int ManifestResourceIdExe = 1;

    /// <summary>mt.exe resource ID for DLL binaries.</summary>
    public const int ManifestResourceIdDll = 2;

    /// <summary>Format version written into module-map.json.</summary>
    public const int ModuleMapFormatVersion = 1;

    /// <summary>Format version of the legacy .dotori.lock (TOML-like).</summary>
    public const int LockFileVersionLegacy = 1;

    /// <summary>Format version written into dotori-lock.json.</summary>
    public const int LockFileVersion = 2;

    // ─── Environment variables ─────────────────────────────────────────────────

    /// <summary>Injected into the build process: full target ID (e.g. macos-arm64).</summary>
    public const string EnvTarget   = "DOTORI_TARGET";

    /// <summary>Injected into the build process: build configuration (debug / release).</summary>
    public const string EnvConfig   = "DOTORI_CONFIG";

    /// <summary>Injected into the build process: target platform (windows, linux, macos, …).</summary>
    public const string EnvPlatform = "DOTORI_PLATFORM";

    /// <summary>Injected into the build process: target CPU architecture (x64, arm64, …).</summary>
    public const string EnvArch     = "DOTORI_ARCH";

    /// <summary>Prefix for per-option environment variables (e.g. DOTORI_OPTION_SIMD).</summary>
    public const string EnvOptionPrefix = "DOTORI_OPTION_";

    /// <summary>Remote build server address (fallback env var for --remote).</summary>
    public const string EnvServer = "DOTORI_SERVER";

    /// <summary>Override compiler path / kind (clang / msvc / absolute path).</summary>
    public const string EnvCxx = "CXX";

    /// <summary>Fallback compiler override when CXX is not set.</summary>
    public const string EnvCc = "CC";

    /// <summary>Android NDK root directory override.</summary>
    public const string EnvAndroidNdkHome = "ANDROID_NDK_HOME";

    /// <summary>Android SDK root directory (NDK resolved under ndk/ subdirectory).</summary>
    public const string EnvAndroidHome = "ANDROID_HOME";

    /// <summary>Emscripten SDK root directory.</summary>
    public const string EnvEmsdkPath = "EMSDK_PATH";

    /// <summary>MinGW sysroot directory override for cross-compilation to Windows.</summary>
    public const string EnvMinGWSysroot = "MINGW_SYSROOT";
}
