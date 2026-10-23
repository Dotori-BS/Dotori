using Dotori.Core.Build;
using Dotori.Core.Linker;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class LldLinkerTests
{
    private static FlatProjectModel MakeModel(
        string name = "MyApp",
        ProjectType type = ProjectType.Executable,
        RuntimeLink runtimeLink = RuntimeLink.Static,
        StdlibType? stdlib = null,
        bool lto = false,
        string? targetTriple = null)
    {
        return new FlatProjectModel
        {
            Name       = name,
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
            Type       = type,
            RuntimeLink = runtimeLink,
            Stdlib     = stdlib,
            Lto        = lto,
        };
    }

    private static ToolchainInfo MakeToolchain(
        string targetTriple = "x86_64-unknown-linux-gnu",
        string? sysRoot = null)
    {
        return new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = targetTriple,
            SysRoot      = sysRoot,
        };
    }

    [TestMethod]
    public void LinkFlags_BasicExecutable_ContainsTargetAndOutput()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain("x86_64-unknown-linux-gnu");
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");

        var flagStr = string.Join(" ", flags);
        Assert.Contains("--target=x86_64-unknown-linux-gnu", flagStr, "Should contain target triple");
        Assert.Contains("-o \"/out/app\"", flagStr, "Should contain output file");
        Assert.Contains("-fuse-ld=lld", flagStr, "Should use lld");
    }

    [TestMethod]
    public void LinkFlags_SharedLibrary_ContainsSharedFlag()
    {
        var model    = MakeModel(type: ProjectType.SharedLibrary);
        var toolchain = MakeToolchain();
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/libmyapp.so");

        Assert.IsTrue(flags.Contains("-shared"), "Should contain -shared");
    }

    [TestMethod]
    public void LinkFlags_WithLto_ContainsFlto()
    {
        var model    = MakeModel(lto: true);
        var toolchain = MakeToolchain();
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Contains("-flto"), "Should contain -flto");
    }

    [TestMethod]
    public void LinkFlags_StaticLibStdCxx_ContainsStaticFlags()
    {
        var model    = MakeModel(runtimeLink: RuntimeLink.Static, stdlib: StdlibType.LibStdCxx);
        var toolchain = MakeToolchain();
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Contains("-static-libgcc"),   "Should contain -static-libgcc");
        Assert.IsTrue(flags.Contains("-static-libstdc++"), "Should contain -static-libstdc++");
    }

    [TestMethod]
    public void LinkFlags_StaticLibCxx_ContainsLibCxxAbi()
    {
        var model    = MakeModel(runtimeLink: RuntimeLink.Static, stdlib: StdlibType.LibCxx);
        var toolchain = MakeToolchain();
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Contains("-static-libstdc++"), "Should contain -static-libstdc++");
        Assert.IsTrue(flags.Contains("-lc++abi"), "Should contain -lc++abi");
    }

    [TestMethod]
    public void LinkFlags_MuslTarget_ContainsStaticFlag()
    {
        var model    = MakeModel(runtimeLink: RuntimeLink.Static);
        var toolchain = MakeToolchain("x86_64-unknown-linux-musl");
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Contains("-static"), "musl + static should add -static");
    }

    [TestMethod]
    public void LinkFlags_GccCrossCompiler_OmitsClangDriverFlags()
    {
        var model = MakeModel(runtimeLink: RuntimeLink.Static);
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/opt/musl/bin/x86_64-linux-musl-g++",
            LinkerPath   = "/opt/musl/bin/x86_64-linux-musl-g++",
            TargetTriple = "x86_64-linux-musl",
        };

        var flags = LldLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsFalse(flags.Any(f => f.StartsWith("--target=")));
        Assert.IsFalse(flags.Contains("-fuse-ld=lld"));
        Assert.IsTrue(flags.Contains("-static"));
    }

    [TestMethod]
    public void LinkFlags_WasmBare_ContainsExportFlags()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain("wasm32-unknown-unknown");
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app.wasm");

        Assert.IsTrue(flags.Contains("-Wl,--no-entry"),   "Should contain --no-entry");
        Assert.IsTrue(flags.Contains("-Wl,--export-all"), "Should contain --export-all");
    }

    [TestMethod]
    public void LinkFlags_WithSysroot_ContainsSysrootFlag()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain(sysRoot: "/sysroot");
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Any(f => f.Contains("--sysroot=")), "Should contain sysroot");
    }

    [TestMethod]
    public void MakeLinkJob_ProducesCorrectJob()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain();
        var flags    = LldLinker.LinkFlags(model, toolchain, "/out/app");
        var job      = LldLinker.MakeLinkJob(new[] { "a.o", "b.o" }, "/out/app", flags);

        Assert.AreEqual("/out/app", job.OutputFile);
        Assert.HasCount(2, job.InputFiles);
        Assert.IsTrue(job.Args.Any(a => a.Contains("a.o")));
    }

    [TestMethod]
    public void LinkFlags_UserLinkFlags_AppendedAfterDotoriFlags()
    {
        var model = new FlatProjectModel
        {
            Name       = "MyApp",
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
        };
        model.LinkFlags.Add("-Wl,--as-needed");
        model.LinkFlags.Add("-Wl,--gc-sections");

        var toolchain = MakeToolchain("x86_64-unknown-linux-gnu");
        var flags = LldLinker.LinkFlags(model, toolchain, "/out/app");

        // dotori flags should appear first
        var targetIdx  = flags.ToList().FindIndex(f => f.StartsWith("--target="));
        var outputIdx  = flags.ToList().FindIndex(f => f.StartsWith("-o "));
        var userFlag1  = flags.ToList().FindIndex(f => f == "-Wl,--as-needed");
        var userFlag2  = flags.ToList().FindIndex(f => f == "-Wl,--gc-sections");

        Assert.IsGreaterThanOrEqualTo(0, targetIdx, "Should contain target triple");
        Assert.IsGreaterThan(outputIdx, userFlag1, "User flags should come after dotori flags");
        Assert.IsGreaterThan(userFlag1, userFlag2, "User flags should preserve order");
    }
}

[TestClass]
public sealed class MsvcStaticLibraryTests
{
    [TestMethod]
    public void LinkFlags_StaticLibrary_ContainOnlyLibrarianOptions()
    {
        var model = new FlatProjectModel
        {
            Name       = "MyLib",
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
            Type       = ProjectType.StaticLibrary,
        };
        model.Links.Add("system-lib");
        model.DebugInfo = DebugInfoLevel.Full;

        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = "cl.exe",
            LinkerPath   = "link.exe",
            TargetTriple = "x86_64-pc-windows-msvc",
        };

        var flags = MsvcLinker.LinkFlags(model, toolchain, "debug", "out/MyLib.lib");

        CollectionAssert.Contains(flags.ToArray(), "/nologo");
        CollectionAssert.Contains(flags.ToArray(), "/OUT:\"out/MyLib.lib\"");
        Assert.IsFalse(flags.Contains("/LIB"));
        Assert.IsFalse(flags.Contains("/DEBUG"));
        Assert.IsFalse(flags.Any(flag => flag.StartsWith("/LIBPATH:", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(flags.Contains("\"system-lib.lib\""));
    }
}

[TestClass]
public sealed class AppleLinkerTests
{
    private static FlatProjectModel MakeModel(
        ProjectType type = ProjectType.Executable,
        string? macosMin = null,
        string? iosMin = null,
        string? tvosMin = null,
        string? watchosMin = null,
        bool lto = false)
    {
        var model = new FlatProjectModel
        {
            Name       = "MyApp",
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
            Type       = type,
            Lto        = lto,
            MacosMin   = macosMin,
            IosMin     = iosMin,
            TvosMin    = tvosMin,
            WatchosMin = watchosMin,
        };
        return model;
    }

    private static ToolchainInfo MakeToolchain(
        string targetTriple = "arm64-apple-macosx14.0",
        string? appleSdk = null)
    {
        return new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = targetTriple,
            AppleSdk     = appleSdk,
        };
    }

    [TestMethod]
    public void IsAppleTarget_MacosTriple_ReturnsTrue()
    {
        var tc = MakeToolchain("arm64-apple-macosx14.0");
        Assert.IsTrue(AppleLinker.IsAppleTarget(tc));
    }

    [TestMethod]
    public void IsAppleTarget_IosTriple_ReturnsTrue()
    {
        var tc = MakeToolchain("arm64-apple-ios16.0");
        Assert.IsTrue(AppleLinker.IsAppleTarget(tc));
    }

    [TestMethod]
    public void IsAppleTarget_LinuxTriple_ReturnsFalse()
    {
        var tc = MakeToolchain("x86_64-unknown-linux-gnu");
        Assert.IsFalse(AppleLinker.IsAppleTarget(tc));
    }

    [TestMethod]
    public void LinkFlags_BasicMacos_ContainsEssentialFlags()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain("arm64-apple-macosx14.0");
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/MyApp");

        var flagStr = string.Join(" ", flags);
        Assert.Contains("--target=arm64-apple-macosx14.0", flagStr, "Should contain target triple");
        Assert.Contains("-o \"/out/MyApp\"", flagStr, "Should contain output");
        Assert.Contains("-stdlib=libc++", flagStr, "Apple always uses libc++");
    }

    [TestMethod]
    public void LinkFlags_SharedLibrary_ContainsDynamiclib()
    {
        var model    = MakeModel(type: ProjectType.SharedLibrary);
        var toolchain = MakeToolchain("arm64-apple-macosx14.0");
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/libMyApp.dylib");

        Assert.IsTrue(flags.Contains("-dynamiclib"), "Should contain -dynamiclib for dylib");
    }

    [TestMethod]
    public void LinkFlags_WithAppleSdk_ContainsIsysroot()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain(appleSdk: "/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk");
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Any(f => f.Contains("-isysroot")), "Should contain -isysroot");
    }

    [TestMethod]
    public void LinkFlags_MacosMin_ContainsMinVersionFlag()
    {
        var model    = MakeModel(macosMin: "12.0");
        var toolchain = MakeToolchain("arm64-apple-macosx14.0");
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Any(f => f.Contains("-mmacosx-version-min=12.0")),
            "Should contain macOS min version flag");
    }

    [TestMethod]
    public void LinkFlags_IosMin_ContainsIphoneosMinVersion()
    {
        var model    = MakeModel(iosMin: "15.0");
        var toolchain = MakeToolchain("arm64-apple-ios15.0");
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Any(f => f.Contains("-miphoneos-version-min=15.0")),
            "Should contain iOS min version flag");
    }

    [TestMethod]
    public void LinkFlags_IosSimulator_ContainsSimulatorMinVersion()
    {
        var model    = MakeModel(iosMin: "15.0");
        var toolchain = MakeToolchain("arm64-apple-ios15.0-simulator");
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Any(f => f.Contains("-mios-simulator-version-min=15.0")),
            "Should contain iOS simulator min version flag");
    }

    [TestMethod]
    public void LinkFlags_WithLto_ContainsFlto()
    {
        var model    = MakeModel(lto: true);
        var toolchain = MakeToolchain();
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/app");

        Assert.IsTrue(flags.Contains("-flto"), "Should contain -flto");
    }

    [TestMethod]
    public void MakeLinkJob_ProducesCorrectJob()
    {
        var model    = MakeModel();
        var toolchain = MakeToolchain();
        var flags    = AppleLinker.LinkFlags(model, toolchain, "/out/app");
        var job      = AppleLinker.MakeLinkJob(new[] { "a.o", "b.o" }, "/out/app", flags);

        Assert.AreEqual("/out/app", job.OutputFile);
        Assert.HasCount(2, job.InputFiles);
    }

    [TestMethod]
    public void LinkFlags_UserLinkFlags_AppendedAfterDotoriFlags()
    {
        var model = MakeModel();
        model.LinkFlags.Add("-Wl,-rpath,@executable_path/lib");

        var toolchain = MakeToolchain("arm64-apple-macosx14.0");
        var flags = AppleLinker.LinkFlags(model, toolchain, "/out/app");

        var outputIdx  = flags.ToList().FindIndex(f => f.StartsWith("-o "));
        var userFlagIdx = flags.ToList().FindIndex(f => f == "-Wl,-rpath,@executable_path/lib");

        Assert.IsGreaterThan(outputIdx, userFlagIdx, "User link flags should come after dotori-generated flags");
    }
}

// ── Phase 1-M: clang-cl -imsvc flags ────────────────────────────────────────

[TestClass]
public sealed class ClangClFlagsTests
{
    private static FlatProjectModel MakeModel(string projectDir = "/tmp/project")
    {
        return new FlatProjectModel
        {
            Name       = "MyLib",
            ProjectDir = projectDir,
            DotoriPath = Path.Combine(projectDir, ".dotori"),
            Type       = ProjectType.Executable,
        };
    }

    [TestMethod]
    public void CompileFlags_ClangCl_ContainsImsvcFlags()
    {
        var vcToolsDir = Path.Combine("MSVC", "Tools", "14.39");
        var winSdkDir  = Path.Combine("WinSDK", "10");
        var msvcPaths = new MsvcPaths
        {
            VcToolsDir   = vcToolsDir,
            WinSdkDir    = winSdkDir,
            WinSdkVer    = "10.0.22621.0",
            Architecture = "x64",
        };

        // Use Path.Combine so the test works cross-platform (macOS/Linux CI)
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = Path.Combine("LLVM", "bin", "clang-cl.exe"),
            LinkerPath   = Path.Combine("LLVM", "bin", "lld-link.exe"),
            TargetTriple = "x86_64-pc-windows-msvc",
            Msvc         = msvcPaths,
        };

        Assert.IsTrue(toolchain.IsClangCl, "Pre-condition: toolchain should be clang-cl");

        var flags = MsvcDriver.CompileFlags(MakeModel(), toolchain, "debug", "obj");
        var flagStr = string.Join(" ", flags);

        Assert.Contains("-imsvc", flagStr, "clang-cl compile flags must contain -imsvc for Windows SDK headers");
        Assert.Contains(vcToolsDir, flagStr, "Should include VcToolsDir include path");
        Assert.Contains("ucrt", flagStr, "Should include ucrt include path");
        Assert.Contains("um",   flagStr, "Should include um include path");
        Assert.Contains("shared", flagStr, "Should include shared include path");
    }

    [TestMethod]
    public void CompileFlags_RegularCl_DoesNotContainImsvc()
    {
        var msvcPaths = new MsvcPaths
        {
            VcToolsDir   = Path.Combine("MSVC", "Tools", "14.39"),
            WinSdkDir    = Path.Combine("WinSDK", "10"),
            WinSdkVer    = "10.0.22621.0",
            Architecture = "x64",
        };

        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = Path.Combine("MSVC", "bin", "cl.exe"),
            LinkerPath   = Path.Combine("MSVC", "bin", "link.exe"),
            TargetTriple = "x86_64-pc-windows-msvc",
            Msvc         = msvcPaths,
        };

        Assert.IsFalse(toolchain.IsClangCl, "Pre-condition: toolchain should be cl.exe, not clang-cl");

        var flags = MsvcDriver.CompileFlags(MakeModel(), toolchain, "debug", "obj");
        var flagStr = string.Join(" ", flags);

        Assert.DoesNotContain("-imsvc", flagStr, "cl.exe should NOT have -imsvc (it finds SDK headers automatically)");
    }
}

// ── Phase 1-M: MinGW static library output name ─────────────────────────────

[TestClass]
public sealed class MinGWOutputNameTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BuildPlanner MakePlanner(ProjectType type, bool isMinGW, string targetId = "windows-x64")
    {
        var model = new FlatProjectModel
        {
            Name       = "MyLib",
            ProjectDir = _tempDir,
            DotoriPath = Path.Combine(_tempDir, ".dotori"),
            Type       = type,
        };

        var toolchain = isMinGW
            ? new ToolchainInfo
            {
                Kind         = CompilerKind.Clang,
                CompilerPath = "/usr/bin/x86_64-w64-mingw32-clang++",
                LinkerPath   = "/usr/bin/x86_64-w64-mingw32-clang++",
                TargetTriple = "x86_64-w64-mingw32",
            }
            : new ToolchainInfo
            {
                Kind         = CompilerKind.Msvc,
                CompilerPath = Path.Combine("MSVC", "cl.exe"),
                LinkerPath   = Path.Combine("MSVC", "link.exe"),
                TargetTriple = "x86_64-pc-windows-msvc",
                Msvc         = new MsvcPaths
                {
                    VcToolsDir   = "MSVC",
                    WinSdkDir    = "",
                    WinSdkVer    = "",
                    Architecture = "x64",
                    LibExePath   = Path.Combine("MSVC", "lib.exe"),
                },
            };

        return new BuildPlanner(model, toolchain, "debug", targetId);
    }

    [TestMethod]
    public void MinGW_StaticLibrary_OutputName_IsLibDotA()
    {
        var planner = MakePlanner(ProjectType.StaticLibrary, isMinGW: true);
        var job = planner.PlanLinkJob(new[] { "a.o", "b.o" });

        Assert.IsNotNull(job, "Static library should produce a link job");
        Assert.IsTrue(job.OutputFile.EndsWith("libMyLib.a", StringComparison.OrdinalIgnoreCase),
            $"MinGW static library should use libXxx.a convention, got: {job.OutputFile}");
    }

    [TestMethod]
    public void Msvc_StaticLibrary_OutputName_IsDotLib()
    {
        var planner = MakePlanner(ProjectType.StaticLibrary, isMinGW: false);
        var job = planner.PlanLinkJob(new[] { "a.obj", "b.obj" });

        Assert.IsNotNull(job, "Static library should produce a link job");
        Assert.IsTrue(job.OutputFile.EndsWith("MyLib.lib", StringComparison.OrdinalIgnoreCase),
            $"MSVC static library should use Xxx.lib convention, got: {job.OutputFile}");
        Assert.AreEqual(Path.Combine("MSVC", "lib.exe"), planner.GetLinkerPath(),
            "MSVC static libraries must be created by lib.exe, not link.exe.");
    }
}
