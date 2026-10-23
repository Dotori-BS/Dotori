using System.Runtime.InteropServices;
using Dotori.Core.Toolchain;

namespace Dotori.Tests.Graph;

[TestClass]
public sealed class ToolchainDetectorTests
{
    [TestMethod]
    public void DetectAvailable_DoesNotThrow()
    {
        // Smoke test: should not throw regardless of host environment
        var available = ToolchainDetector.DetectAvailable();
        Assert.IsNotNull(available);
    }

    [TestMethod]
    public void Detect_UnknownTarget_ReturnsNull()
    {
        var result = ToolchainDetector.Detect("nonexistent-target-xyz");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Detect_ValidTargetOnHost_ReturnsInfoOrNull()
    {
        // On macOS we expect to find clang for macos-arm64 or macos-x64
        // On Linux  we expect to find clang for linux-x64 (if installed)
        // On Windows we expect MSVC or clang
        // This just verifies it doesn't throw
        var targets = new[] { "macos-arm64", "macos-x64", "linux-x64", "windows-x64" };
        foreach (var target in targets)
        {
            var result = ToolchainDetector.Detect(target);
            // result may be null if not on the matching OS — that's fine
            if (result is not null)
            {
                Assert.IsFalse(string.IsNullOrEmpty(result.CompilerPath),
                    $"CompilerPath should not be empty for {target}");
                Assert.IsFalse(string.IsNullOrEmpty(result.TargetTriple),
                    $"TargetTriple should not be empty for {target}");
            }
        }
    }

    // ── Phase 1-M: IsClangCl / IsMinGW property tests ───────────────────────

    [TestMethod]
    public void IsClangCl_True_WhenKindMsvcAndCompilerNameClangCl()
    {
        // Use path.Combine so the test works cross-platform (macOS/Linux CI as well)
        var compilerPath = Path.Combine("llvm", "bin", "clang-cl.exe");
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = compilerPath,
            LinkerPath   = Path.Combine("llvm", "bin", "lld-link.exe"),
            TargetTriple = "x86_64-pc-windows-msvc",
        };

        Assert.IsTrue(toolchain.IsClangCl, "Should be clang-cl when Kind==Msvc and compiler name is clang-cl");
    }

    [TestMethod]
    public void IsClangCl_False_WhenKindMsvcAndCompilerNameCl()
    {
        var compilerPath = Path.Combine("msvc", "bin", "cl.exe");
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = compilerPath,
            LinkerPath   = Path.Combine("msvc", "bin", "link.exe"),
            TargetTriple = "x86_64-pc-windows-msvc",
        };

        Assert.IsFalse(toolchain.IsClangCl, "Should not be clang-cl when compiler is cl.exe");
    }

    [TestMethod]
    public void IsClangCl_False_WhenKindClang()
    {
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = "x86_64-unknown-linux-gnu",
        };

        Assert.IsFalse(toolchain.IsClangCl, "Clang (not clang-cl) should not be IsClangCl");
    }

    [TestMethod]
    public void IsMinGW_True_WhenKindClangAndTripleContainsMingw()
    {
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = "x86_64-w64-mingw32",
        };

        Assert.IsTrue(toolchain.IsMinGW, "Should be MinGW when triple contains 'mingw'");
    }

    [TestMethod]
    public void IsMinGW_False_WhenKindMsvc()
    {
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = @"C:\LLVM\clang-cl.exe",
            LinkerPath   = @"C:\LLVM\lld-link.exe",
            TargetTriple = "x86_64-w64-mingw32",  // triple doesn't matter if Kind != Clang
        };

        Assert.IsFalse(toolchain.IsMinGW, "MSVC kind should never be IsMinGW");
    }

    [TestMethod]
    public void IsMinGW_False_WhenKindClangAndTripleNotMingw()
    {
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = "x86_64-unknown-linux-gnu",
        };

        Assert.IsFalse(toolchain.IsMinGW, "Linux Clang should not be IsMinGW");
    }

    // ── CC/CXX env var & --compiler path override ────────────────────────────

    [TestMethod]
    public void IsKnownKindName_ReturnsTrue_ForMsvcAndClang()
    {
        Assert.IsTrue(ToolchainDetector.IsKnownKindName("msvc"));
        Assert.IsTrue(ToolchainDetector.IsKnownKindName("clang"));
        Assert.IsTrue(ToolchainDetector.IsKnownKindName("MSVC"),  "case-insensitive");
        Assert.IsTrue(ToolchainDetector.IsKnownKindName("Clang"), "case-insensitive");
    }

    [TestMethod]
    public void IsKnownKindName_ReturnsFalse_ForPathsAndOtherNames()
    {
        Assert.IsFalse(ToolchainDetector.IsKnownKindName("/usr/bin/clang++"));
        Assert.IsFalse(ToolchainDetector.IsKnownKindName("clang++"));
        Assert.IsFalse(ToolchainDetector.IsKnownKindName("clang-18"));
        Assert.IsFalse(ToolchainDetector.IsKnownKindName("cl.exe"));
        Assert.IsFalse(ToolchainDetector.IsKnownKindName(""));
    }

    [TestMethod]
    public void GuessKindFromPath_Msvc_ForClAndClangCl()
    {
        Assert.AreEqual(CompilerKind.Msvc, ToolchainDetector.GuessKindFromPath("cl.exe"));
        Assert.AreEqual(CompilerKind.Msvc, ToolchainDetector.GuessKindFromPath(@"C:\LLVM\bin\clang-cl.exe"));
        Assert.AreEqual(CompilerKind.Msvc, ToolchainDetector.GuessKindFromPath("/usr/bin/clang-cl"));
    }

    [TestMethod]
    public void GuessKindFromPath_Emscripten_ForEmcc()
    {
        Assert.AreEqual(CompilerKind.Emscripten, ToolchainDetector.GuessKindFromPath("emcc"));
        Assert.AreEqual(CompilerKind.Emscripten, ToolchainDetector.GuessKindFromPath("/emsdk/emcc"));
        Assert.AreEqual(CompilerKind.Emscripten, ToolchainDetector.GuessKindFromPath("em++"));
    }

    [TestMethod]
    public void GuessKindFromPath_Clang_ForOtherNames()
    {
        Assert.AreEqual(CompilerKind.Clang, ToolchainDetector.GuessKindFromPath("clang++"));
        Assert.AreEqual(CompilerKind.Clang, ToolchainDetector.GuessKindFromPath("/usr/bin/clang++-18"));
        Assert.AreEqual(CompilerKind.Clang, ToolchainDetector.GuessKindFromPath("g++"));
    }

    [TestMethod]
    public void ApplyCompilerOverride_ReplacesKindAndCompilerPath()
    {
        var original = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = "x86_64-unknown-linux-gnu",
            SysRoot      = "/sysroot",
            AppleSdk     = null,
            Msvc         = null,
        };

        var overridden = ToolchainDetector.ApplyCompilerOverride(original, "/opt/llvm/bin/clang++-18");

        Assert.AreEqual(CompilerKind.Clang,                   overridden.Kind);
        Assert.AreEqual("/opt/llvm/bin/clang++-18",           overridden.CompilerPath);
        Assert.AreEqual("/opt/llvm/bin/clang++-18",           overridden.LinkerPath,
            "Clang-family overrides must use the same driver for linking.");
        Assert.AreEqual(original.TargetTriple,                overridden.TargetTriple, "TargetTriple preserved");
        Assert.AreEqual(original.SysRoot,                     overridden.SysRoot,      "SysRoot preserved");
    }

    [TestMethod]
    public void ApplyCompilerOverride_UpdatesKindForClangCl()
    {
        var original = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/ld.lld",
            TargetTriple = "x86_64-pc-windows-msvc",
        };

        var overridden = ToolchainDetector.ApplyCompilerOverride(
            original, @"C:\LLVM\bin\clang-cl.exe");

        Assert.AreEqual(CompilerKind.Msvc, overridden.Kind, "Kind should become Msvc for clang-cl");
        Assert.AreEqual(@"C:\LLVM\bin\clang-cl.exe", overridden.CompilerPath);
        Assert.AreEqual(@"C:\LLVM\bin\clang-cl.exe", overridden.LinkerPath,
            "When switching from a Clang toolchain to clang-cl, use the MSVC-compatible driver for linking.");
    }

    [TestMethod]
    public void ApplyCompilerOverride_ClangCl_PreservesDetectedMsvcLinker()
    {
        var original = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = @"C:\MSVC\cl.exe",
            LinkerPath   = @"C:\MSVC\link.exe",
            TargetTriple = "x86_64-pc-windows-msvc",
        };

        var overridden = ToolchainDetector.ApplyCompilerOverride(
            original, @"C:\LLVM\bin\clang-cl.exe");

        Assert.AreEqual(CompilerKind.Msvc, overridden.Kind);
        Assert.AreEqual(original.LinkerPath, overridden.LinkerPath,
            "A detected MSVC linker is compatible with clang-cl's MSVC-style arguments.");
    }

    [TestMethod]
    public void ResolveAsCompilerPath_ReturnsNull_ForNonExistentAbsolutePath()
    {
        var result = ToolchainDetector.ResolveAsCompilerPath("/nonexistent/path/to/clang++");
        Assert.IsNull(result, "Should return null when file does not exist");
    }

    [TestMethod]
    public void ResolveAsCompilerPath_ReturnsNull_ForNullOrEmpty()
    {
        Assert.IsNull(ToolchainDetector.ResolveAsCompilerPath(""));
        Assert.IsNull(ToolchainDetector.ResolveAsCompilerPath("   "));
    }

    [TestMethod]
    public void GetEnvCompilerPath_ReturnsNull_WhenNeitherCxxNorCcSet()
    {
        // Save and clear env vars
        var savedCxx = Environment.GetEnvironmentVariable("CXX");
        var savedCc  = Environment.GetEnvironmentVariable("CC");
        try
        {
            Environment.SetEnvironmentVariable("CXX", null);
            Environment.SetEnvironmentVariable("CC",  null);

            var result = ToolchainDetector.GetEnvCompilerPath();
            Assert.IsNull(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CXX", savedCxx);
            Environment.SetEnvironmentVariable("CC",  savedCc);
        }
    }

    [TestMethod]
    public void GetEnvCompilerPath_ReturnsNull_WhenCxxPointsToNonExistentFile()
    {
        var saved = Environment.GetEnvironmentVariable("CXX");
        try
        {
            Environment.SetEnvironmentVariable("CXX", "/nonexistent/compiler");
            var result = ToolchainDetector.GetEnvCompilerPath();
            Assert.IsNull(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CXX", saved);
        }
    }

    [TestMethod]
    public void Detect_CxxEnvVar_OverridesCompilerPath()
    {
        // Use the host's own dotnet runtime binary as a stand-in "compiler"
        // executable that is guaranteed to exist on the current machine.
        var fakeBinary = Environment.ProcessPath ?? typeof(object).Assembly.Location;
        if (fakeBinary is null || !File.Exists(fakeBinary))
            Assert.Inconclusive("Cannot determine a valid host executable for this test.");

        // Pick a valid target for the current OS so detection normally succeeds.
        var target = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? "macos-arm64"
                   : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)   ? "linux-x64"
                   : "windows-x64";

        // Only run if the toolchain is available on this machine.
        var baseline = ToolchainDetector.Detect(target);
        if (baseline is null)
            Assert.Inconclusive($"No toolchain detected for {target} — skipping override test.");

        var savedCxx = Environment.GetEnvironmentVariable("CXX");
        var savedCc  = Environment.GetEnvironmentVariable("CC");
        try
        {
            Environment.SetEnvironmentVariable("CC",  null);
            Environment.SetEnvironmentVariable("CXX", fakeBinary);

            var result = ToolchainDetector.Detect(target);
            Assert.IsNotNull(result, "Detect should succeed even with CXX override");
            Assert.AreEqual(fakeBinary, result.CompilerPath,
                "CompilerPath should be replaced by the CXX env var value");
            // Other metadata (TargetTriple) should be preserved from normal detection.
            Assert.AreEqual(baseline.TargetTriple, result.TargetTriple,
                "TargetTriple should be preserved from baseline detection");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CXX", savedCxx);
            Environment.SetEnvironmentVariable("CC",  savedCc);
        }
    }
}
