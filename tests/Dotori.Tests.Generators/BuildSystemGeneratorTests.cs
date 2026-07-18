using Dotori.Core.Generators;
using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Tests.Generators;

[TestClass]
public sealed class BuildSystemGeneratorTests
{
    private static FlatProjectModel MakeModel(
        string name = "MyApp",
        ProjectType type = ProjectType.Executable,
        CxxStd std = CxxStd.Cxx20) =>
        new()
        {
            Name       = name,
            ProjectDir = "/tmp/project",
            DotoriPath = "/tmp/project/.dotori",
            Type       = type,
            Std        = std,
        };

    private static FlatProjectModel MakeFullModel()
    {
        var model = MakeModel();
        model.Defines.Add("MY_DEFINE");
        model.Defines.Add("VERSION=1");
        model.CompileFlags.Add("-fno-exceptions");
        model.Links.Add("pthread");
        model.Headers.Add(new HeaderItem(isPublic: true,  path: "include/"));
        model.Headers.Add(new HeaderItem(isPublic: false, path: "src/internal/"));
        return model;
    }

    // ─── CMakeGenerator ────────────────────────────────────────────────────

    [TestMethod]
    public void CMake_Executable_ContainsAddExecutable()
    {
        var gen    = new CMakeGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        Assert.HasCount(1, result);
        Assert.AreEqual("CMakeLists.txt", result[0].RelativePath);
        StringAssert.Contains(result[0].Content, "add_executable(MyApp");
    }

    [TestMethod]
    public void CMake_Test_RegistersCtestTarget()
    {
        var result = new CMakeGenerator().Generate(
            MakeModel(type: ProjectType.Test), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "add_executable(MyApp");
        StringAssert.Contains(result[0].Content, "add_test(NAME MyApp COMMAND MyApp)");
    }

    [TestMethod]
    public void CMake_StaticLibrary_ContainsAddLibraryStatic()
    {
        var gen    = new CMakeGenerator();
        var result = gen.Generate(MakeModel(type: ProjectType.StaticLibrary), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "add_library(MyApp STATIC");
    }

    [TestMethod]
    public void CMake_SharedLibrary_ContainsAddLibraryShared()
    {
        var gen    = new CMakeGenerator();
        var result = gen.Generate(MakeModel(type: ProjectType.SharedLibrary), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "add_library(MyApp SHARED");
    }

    [TestMethod]
    public void CMake_HeaderOnly_ContainsAddLibraryInterface()
    {
        var gen    = new CMakeGenerator();
        var result = gen.Generate(MakeModel(type: ProjectType.HeaderOnly), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "add_library(MyApp INTERFACE");
    }

    [TestMethod]
    public void CMake_CxxStd_WritesCorrectStandard()
    {
        var gen    = new CMakeGenerator();
        var result = gen.Generate(MakeModel(std: CxxStd.Cxx17), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "CXX_STANDARD 17");
    }

    [TestMethod]
    public void CMake_Defines_WritesTargetCompileDefinitions()
    {
        var gen    = new CMakeGenerator();
        var model  = MakeFullModel();
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "target_compile_definitions(MyApp PRIVATE");
        StringAssert.Contains(result[0].Content, "MY_DEFINE");
    }

    [TestMethod]
    public void CMake_Links_WritesTargetLinkLibraries()
    {
        var gen    = new CMakeGenerator();
        var model  = MakeFullModel();
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "target_link_libraries(MyApp PRIVATE");
        StringAssert.Contains(result[0].Content, "pthread");
    }

    [TestMethod]
    public void CMake_PublicHeaders_WritesPublicIncludeDirectories()
    {
        var gen    = new CMakeGenerator();
        var model  = MakeFullModel();
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "target_include_directories(MyApp PUBLIC");
        StringAssert.Contains(result[0].Content, "include/");
    }

    [TestMethod]
    public void CMake_PrivateHeaders_WritesPrivateIncludeDirectories()
    {
        var gen    = new CMakeGenerator();
        var model  = MakeFullModel();
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "target_include_directories(MyApp PRIVATE");
        StringAssert.Contains(result[0].Content, "src/internal/");
    }

    [TestMethod]
    public void CMake_GitDependency_WritesFetchContent()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.Dependencies.Add(new DependencyItem("fmt", new ComplexDependency
        {
            Git = "https://github.com/fmtlib/fmt.git",
            Tag = "10.2.0",
        }));
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "include(FetchContent)");
        StringAssert.Contains(result[0].Content, "FetchContent_Declare(fmt");
        StringAssert.Contains(result[0].Content, "https://github.com/fmtlib/fmt.git");
    }

    [TestMethod]
    public void CMake_PathDependency_WritesAddSubdirectory()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.Dependencies.Add(new DependencyItem("mylib", new ComplexDependency
        {
            Path = "../mylib",
        }));
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "add_subdirectory(../mylib)");
    }

    [TestMethod]
    public void CMake_VersionDependency_WritesComment()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.Dependencies.Add(new DependencyItem("boost", new VersionDependency("1.85.0")));
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "# find_package(boost 1.85.0 REQUIRED)");
    }

    [TestMethod]
    public void CMake_Pch_WritesTargetPrecompileHeaders()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.Pch = new PchConfig { Header = "include/pch.h" };
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "target_precompile_headers(MyApp PRIVATE");
        StringAssert.Contains(result[0].Content, "include/pch.h");
    }

    [TestMethod]
    public void CMake_UnityBuild_WritesUnityBuildProperty()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.UnityBuild = new UnityBuildConfig { Enabled = true, BatchSize = 16 };
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "UNITY_BUILD ON");
        StringAssert.Contains(result[0].Content, "UNITY_BUILD_BATCH_SIZE 16");
    }

    [TestMethod]
    public void CMake_Lto_WritesInterprocedural()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.Lto = true;
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "INTERPROCEDURAL_OPTIMIZATION ON");
    }

    [TestMethod]
    public void CMake_MacosMin_WritesDeploymentTarget()
    {
        var gen   = new CMakeGenerator();
        var model = MakeModel();
        model.MacosMin = "13.0";
        var result = gen.Generate(model, "both", "macos-arm64");

        StringAssert.Contains(result[0].Content, "CMAKE_OSX_DEPLOYMENT_TARGET");
        StringAssert.Contains(result[0].Content, "13.0");
    }

    // ─── MesonGenerator ────────────────────────────────────────────────────

    [TestMethod]
    public void Meson_Executable_ContainsExecutableFunction()
    {
        var gen    = new MesonGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        Assert.HasCount(1, result);
        Assert.AreEqual("meson.build", result[0].RelativePath);
        StringAssert.Contains(result[0].Content, "executable('MyApp'");
    }

    [TestMethod]
    public void Meson_Test_RegistersTestTarget()
    {
        var result = new MesonGenerator().Generate(
            MakeModel(type: ProjectType.Test), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "executable('MyApp'");
        StringAssert.Contains(result[0].Content, "test('MyApp', MyApp)");
    }

    [TestMethod]
    public void Meson_StaticLibrary_ContainsStaticLibraryFunction()
    {
        var gen    = new MesonGenerator();
        var result = gen.Generate(MakeModel(type: ProjectType.StaticLibrary), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "static_library('MyApp'");
    }

    [TestMethod]
    public void Meson_SharedLibrary_ContainsSharedLibraryFunction()
    {
        var gen    = new MesonGenerator();
        var result = gen.Generate(MakeModel(type: ProjectType.SharedLibrary), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "shared_library('MyApp'");
    }

    [TestMethod]
    public void Meson_CxxStd_WritesProjectDefaultOptions()
    {
        var gen    = new MesonGenerator();
        var result = gen.Generate(MakeModel(std: CxxStd.Cxx23), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "cpp_std=c++23");
    }

    [TestMethod]
    public void Meson_Defines_WritesCppArgs()
    {
        var gen   = new MesonGenerator();
        var model = MakeFullModel();
        var result = gen.Generate(model, "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "cpp_args:");
        StringAssert.Contains(result[0].Content, "-DMY_DEFINE");
    }

    // ─── VcxprojGenerator ──────────────────────────────────────────────────

    [TestMethod]
    public void Vcxproj_Executable_ProducesTwoFiles()
    {
        var gen    = new VcxprojGenerator();
        var result = gen.Generate(MakeModel(), "both", "windows-x64");

        Assert.HasCount(2, result);
        Assert.EndsWith(".vcxproj", result[0].RelativePath);
        Assert.EndsWith(".vcxproj.filters", result[1].RelativePath);
    }

    [TestMethod]
    public void Vcxproj_Executable_ContainsApplication()
    {
        var gen    = new VcxprojGenerator();
        var result = gen.Generate(MakeModel(), "both", "windows-x64");

        StringAssert.Contains(result[0].Content, "<ConfigurationType>Application</ConfigurationType>");
    }

    [TestMethod]
    public void Vcxproj_StaticLibrary_ContainsStaticLibrary()
    {
        var gen    = new VcxprojGenerator();
        var result = gen.Generate(MakeModel(type: ProjectType.StaticLibrary), "both", "windows-x64");

        StringAssert.Contains(result[0].Content, "<ConfigurationType>StaticLibrary</ConfigurationType>");
    }

    [TestMethod]
    public void Vcxproj_CxxStd_WritesLanguageStandard()
    {
        var gen    = new VcxprojGenerator();
        var result = gen.Generate(MakeModel(std: CxxStd.Cxx17), "both", "windows-x64");

        StringAssert.Contains(result[0].Content, "<LanguageStandard>stdcpp17</LanguageStandard>");
    }

    [TestMethod]
    public void Vcxproj_DebugConfig_ContainsDebug()
    {
        var gen    = new VcxprojGenerator();
        var result = gen.Generate(MakeModel(), "both", "windows-x64");

        StringAssert.Contains(result[0].Content, "Debug|x64");
        StringAssert.Contains(result[0].Content, "Release|x64");
    }

    [TestMethod]
    public void Vcxproj_SingleConfig_Debug_OnlyDebug()
    {
        var gen    = new VcxprojGenerator();
        var result = gen.Generate(MakeModel(), "debug", "windows-x64");

        StringAssert.Contains(result[0].Content, "Debug|x64");
        Assert.DoesNotContain("Release|x64", result[0].Content);
    }

    [TestMethod]
    public void Vcxproj_Defines_WritesPreprocessorDefinitions()
    {
        var gen   = new VcxprojGenerator();
        var model = MakeFullModel();
        var result = gen.Generate(model, "both", "windows-x64");

        StringAssert.Contains(result[0].Content, "MY_DEFINE");
    }

    // ─── PbxprojGenerator ──────────────────────────────────────────────────

    [TestMethod]
    public void Pbxproj_ProducesXcodeproj()
    {
        var gen    = new PbxprojGenerator();
        var result = gen.Generate(MakeModel(), "both", "macos-arm64");

        Assert.HasCount(1, result);
        Assert.EndsWith("project.pbxproj", result[0].RelativePath);
    }

    [TestMethod]
    public void Pbxproj_ContainsPbxNativeTarget()
    {
        var gen    = new PbxprojGenerator();
        var result = gen.Generate(MakeModel(), "both", "macos-arm64");

        StringAssert.Contains(result[0].Content, "PBXNativeTarget");
    }

    [TestMethod]
    public void Pbxproj_Executable_ContainsToolProductType()
    {
        var gen    = new PbxprojGenerator();
        var result = gen.Generate(MakeModel(), "both", "macos-arm64");

        StringAssert.Contains(result[0].Content, "com.apple.product-type.tool");
    }

    [TestMethod]
    public void Pbxproj_CxxStd_WritesLanguageStandard()
    {
        var gen    = new PbxprojGenerator();
        var result = gen.Generate(MakeModel(std: CxxStd.Cxx23), "both", "macos-arm64");

        StringAssert.Contains(result[0].Content, "CLANG_CXX_LANGUAGE_STANDARD = \"c++23\"");
    }

    [TestMethod]
    public void Pbxproj_MacosMin_WritesDeploymentTarget()
    {
        var gen   = new PbxprojGenerator();
        var model = MakeModel();
        model.MacosMin = "14.0";
        var result = gen.Generate(model, "both", "macos-arm64");

        StringAssert.Contains(result[0].Content, "MACOSX_DEPLOYMENT_TARGET = 14.0");
    }

    // ─── NinjaGenerator ────────────────────────────────────────────────────

    [TestMethod]
    public void Ninja_SingleConfig_ProducesOneBuildNinja()
    {
        var gen    = new NinjaGenerator();
        var result = gen.Generate(MakeModel(), "debug", "linux-x64");

        Assert.HasCount(1, result);
        Assert.AreEqual("build.ninja", result[0].RelativePath);
    }

    [TestMethod]
    public void Ninja_BothConfigs_ProducesTwoFiles()
    {
        var gen    = new NinjaGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        Assert.HasCount(2, result);
        Assert.IsTrue(result.Any(r => r.RelativePath == "build.debug.ninja"));
        Assert.IsTrue(result.Any(r => r.RelativePath == "build.release.ninja"));
    }

    [TestMethod]
    public void Ninja_ContainsRuleCxx()
    {
        var gen    = new NinjaGenerator();
        var result = gen.Generate(MakeModel(), "debug", "linux-x64");

        StringAssert.Contains(result[0].Content, "rule cxx");
    }

    [TestMethod]
    public void Ninja_CxxFlagsContainStdFlag()
    {
        var gen    = new NinjaGenerator();
        var result = gen.Generate(MakeModel(std: CxxStd.Cxx17), "debug", "linux-x64");

        StringAssert.Contains(result[0].Content, "-std=c++17");
    }

    // ─── MakefileGenerator ─────────────────────────────────────────────────

    [TestMethod]
    public void Makefile_ProducesMakefile()
    {
        var gen    = new MakefileGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        Assert.HasCount(1, result);
        Assert.AreEqual("Makefile", result[0].RelativePath);
    }

    [TestMethod]
    public void Makefile_ContainsAllTarget()
    {
        var gen    = new MakefileGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "all:");
    }

    [TestMethod]
    public void Makefile_ContainsCleanTarget()
    {
        var gen    = new MakefileGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "clean:");
    }

    [TestMethod]
    public void Makefile_CxxFlagsContainStdFlag()
    {
        var gen    = new MakefileGenerator();
        var result = gen.Generate(MakeModel(std: CxxStd.Cxx20), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "-std=c++20");
    }

    [TestMethod]
    public void Makefile_DebugReleaseSwitching_ContainsIfeq()
    {
        var gen    = new MakefileGenerator();
        var result = gen.Generate(MakeModel(), "both", "linux-x64");

        StringAssert.Contains(result[0].Content, "ifeq ($(CONFIG),release)");
    }
}
