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

    private static FlatProjectModel MakeModel(bool forceCxx = false) =>
        new()
        {
            Name       = "MyApp",
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
            ForceCxx   = forceCxx,
        };

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
}
