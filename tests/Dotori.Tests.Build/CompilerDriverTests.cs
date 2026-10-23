using Dotori.Core.Build;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class CompilerDriverTests
{
    private static ToolchainInfo MakeClangToolchain(string triple = "x86_64-unknown-linux-gnu") =>
        new()
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/usr/bin/clang++",
            LinkerPath   = "/usr/bin/clang++",
            TargetTriple = triple,
        };

    private static FlatProjectModel MakeModel(bool forceCxx = false, string name = "MyApp") =>
        new()
        {
            Name       = name,
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
            ForceCxx   = forceCxx,
        };

    [TestMethod]
    public void SwiftDriver_SanitizesModuleNames()
    {
        Assert.AreEqual("My_App", SwiftDriver.SanitizeModuleName("My-App"));
        Assert.AreEqual("_42_tools", SwiftDriver.SanitizeModuleName("42 tools"));
    }

    [TestMethod]
    public void SwiftDriver_PlansModuleObjectAndCxxInterop()
    {
        var model = MakeModel(name: "Mixed-App");
        var job = SwiftDriver.MakeCompileJob(
            model, MakeClangToolchain("arm64-apple-macosx"), "debug", "/tmp/obj",
            ["/tmp/project/a.swift", "/tmp/project/b.swift"], "/usr/bin/swiftc",
            hasCxxSources: true, dependencyModuleDirectories: ["/tmp/dependency"]);

        Assert.AreEqual(Path.Combine("/tmp/obj", "Mixed_App.swift.o"), job.OutputFile);
        Assert.IsTrue(job.Args.Contains("-emit-module"));
        Assert.IsTrue(job.Args.Contains("-cxx-interoperability-mode=default"));
        Assert.IsTrue(job.Args.Contains($"\"{Path.Combine("/tmp/obj", "Mixed_App.swiftmodule")}\""));
        Assert.IsTrue(job.Args.Contains("\"/tmp/dependency\""));
        CollectionAssert.Contains(job.DependencyFiles.ToArray(), "/tmp/project/b.swift");
    }

    // ─── ClangDriver ───────────────────────────────────────────────────────

    [TestMethod]
    public void ClangDriver_ForceCxx_CFile_InjectsXCxxFlag()
    {
        var model    = MakeModel(forceCxx: true);
        var toolchain = MakeClangToolchain();
        var flags    = ClangDriver.CompileFlags(model, toolchain, "debug", "/tmp/obj");
        var job      = ClangDriver.MakeCompileJob("/tmp/project/src/foo.c", "/tmp/obj", flags, cAsCpp: true);
        Assert.IsTrue(job.Args.Any(a => a == "-x c++"),
            "Expected '-x c++' in args for .c file with ForceCxx=true");
    }

    [TestMethod]
    public void ClangDriver_IncludePaths_TrimTrailingDirectorySeparator()
    {
        var model = MakeModel();
        model.Headers.Add(new HeaderItem(isPublic: true, path: "include/"));

        var flags = ClangDriver.CompileFlags(model, MakeClangToolchain(), "debug", "/tmp/obj");
        var includePath = PathUtils.MakeAbsoluteDirectory(model.ProjectDir, "include/");

        CollectionAssert.Contains(flags.ToArray(), $"-I\"{includePath}\"");
        Assert.IsFalse(flags.Any(flag => flag.EndsWith("\\\"", StringComparison.Ordinal)),
            "A quoted include path must not end with a backslash immediately before its closing quote.");
    }

    [TestMethod]
    public void ClangDriver_ForceCxx_CppFile_NoXCxxFlag()
    {
        var model    = MakeModel(forceCxx: true);
        var toolchain = MakeClangToolchain();
        var flags    = ClangDriver.CompileFlags(model, toolchain, "debug", "/tmp/obj");
        var job      = ClangDriver.MakeCompileJob("/tmp/project/src/foo.cpp", "/tmp/obj", flags, cAsCpp: true);
        Assert.IsFalse(job.Args.Any(a => a == "-x c++"),
            "Expected no '-x c++' for .cpp file");
    }

    [TestMethod]
    public void ClangDriver_ForceCxxFalse_CFile_NoXCxxFlag()
    {
        var model    = MakeModel(forceCxx: false);
        var toolchain = MakeClangToolchain();
        var flags    = ClangDriver.CompileFlags(model, toolchain, "debug", "/tmp/obj");
        var job      = ClangDriver.MakeCompileJob("/tmp/project/src/foo.c", "/tmp/obj", flags, cAsCpp: false);
        Assert.IsFalse(job.Args.Any(a => a == "-x c++"),
            "Expected no '-x c++' when ForceCxx=false");
    }

    [TestMethod]
    public void ClangDriver_GccCrossCompiler_OmitsClangTargetFlag()
    {
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Clang,
            CompilerPath = "/opt/musl/bin/x86_64-linux-musl-g++",
            LinkerPath   = "/opt/musl/bin/x86_64-linux-musl-g++",
            TargetTriple = "x86_64-linux-musl",
        };

        var flags = ClangDriver.CompileFlags(MakeModel(), toolchain, "debug", "/tmp/obj");

        Assert.IsFalse(flags.Any(f => f.StartsWith("--target=")));
        Assert.IsTrue(toolchain.IsGccDriver);
    }

    [TestMethod]
    public void CompileDrivers_SameBasenameInDifferentDirectories_UseDistinctOutputs()
    {
        var clang = MakeClangToolchain();
        var clangFlags = ClangDriver.CompileFlags(MakeModel(), clang, "debug", "/tmp/obj");
        var clangA = ClangDriver.MakeCompileJob("/tmp/project/client/main.cpp", "/tmp/obj", clangFlags);
        var clangB = ClangDriver.MakeCompileJob("/tmp/project/server/main.cpp", "/tmp/obj", clangFlags);
        Assert.AreNotEqual(clangA.OutputFile, clangB.OutputFile);

        var emFlags = EmscriptenDriver.CompileFlags(MakeModel(), "/tmp/obj");
        var emA = EmscriptenDriver.MakeCompileJob("/tmp/project/client/main.cpp", "/tmp/obj", emFlags);
        var emB = EmscriptenDriver.MakeCompileJob("/tmp/project/server/main.cpp", "/tmp/obj", emFlags);
        Assert.AreNotEqual(emA.OutputFile, emB.OutputFile);

        var msvc = new ToolchainInfo
        {
            Kind = CompilerKind.Msvc, CompilerPath = "cl.exe", LinkerPath = "link.exe",
            TargetTriple = "x86_64-pc-windows-msvc",
        };
        var msvcFlags = MsvcDriver.CompileFlags(MakeModel(), msvc, "debug", "/tmp/obj");
        var msvcA = MsvcDriver.MakeCompileJob("/tmp/project/client/main.cpp", "/tmp/obj", msvcFlags);
        var msvcB = MsvcDriver.MakeCompileJob("/tmp/project/server/main.cpp", "/tmp/obj", msvcFlags);
        Assert.AreNotEqual(msvcA.OutputFile, msvcB.OutputFile);
        Assert.IsTrue(msvcA.Args.Any(a => a.Contains(msvcA.OutputFile)));
    }

    // ─── MsvcDriver ────────────────────────────────────────────────────────

    [TestMethod]
    public void MsvcDriver_ForceCxx_CFile_InjectsTpFlag()
    {
        var model    = MakeModel(forceCxx: true);
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = "cl.exe",
            LinkerPath   = "link.exe",
            TargetTriple = "x86_64-pc-windows-msvc",
        };
        var flags = MsvcDriver.CompileFlags(model, toolchain, "debug", "/tmp/obj");
        var job   = MsvcDriver.MakeCompileJob("/tmp/project/src/foo.c", "/tmp/obj", flags, cAsCpp: true);
        Assert.IsTrue(job.Args.Any(a => a.StartsWith("/Tp")),
            "Expected '/Tp...' in args for .c file with ForceCxx=true");
        // 일반 파일명 인수(따옴표로 감싼 경로)가 없어야 함
        Assert.IsFalse(job.Args.Any(a => a == "\"/tmp/project/src/foo.c\""),
            "Expected no bare filename arg when /Tp is used");
    }

    [TestMethod]
    public void MsvcDriver_ForceCxx_CppFile_NoTpFlag()
    {
        var model    = MakeModel(forceCxx: true);
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = "cl.exe",
            LinkerPath   = "link.exe",
            TargetTriple = "x86_64-pc-windows-msvc",
        };
        var flags = MsvcDriver.CompileFlags(model, toolchain, "debug", "/tmp/obj");
        var job   = MsvcDriver.MakeCompileJob("/tmp/project/src/foo.cpp", "/tmp/obj", flags, cAsCpp: true);
        Assert.IsFalse(job.Args.Any(a => a.StartsWith("/Tp")),
            "Expected no '/Tp' for .cpp file");
    }

    [TestMethod]
    public void MsvcDriver_IncludePaths_TrimTrailingDirectorySeparator()
    {
        var model = MakeModel();
        model.Headers.Add(new HeaderItem(isPublic: true, path: "include/"));
        var toolchain = new ToolchainInfo
        {
            Kind         = CompilerKind.Msvc,
            CompilerPath = "cl.exe",
            LinkerPath   = "link.exe",
            TargetTriple = "x86_64-pc-windows-msvc",
        };

        var flags = MsvcDriver.CompileFlags(model, toolchain, "debug", "/tmp/obj");
        var includePath = PathUtils.MakeAbsoluteDirectory(model.ProjectDir, "include/");

        CollectionAssert.Contains(flags.ToArray(), $"/I\"{includePath}\"");
        Assert.IsFalse(flags.Any(flag => flag.EndsWith("\\\"", StringComparison.Ordinal)),
            "A quoted include path must not end with a backslash immediately before its closing quote.");
    }
}
