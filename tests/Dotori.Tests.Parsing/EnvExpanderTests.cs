using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Tests.Parsing;

[TestClass]
public sealed class EnvExpanderTests
{
    // ─── EnvExpander unit tests ─────────────────────────────────────────────

    [TestMethod]
    public void Expand_NoPlaceholder_ReturnsSameString()
    {
        Assert.AreEqual("-march=native", EnvExpander.Expand("-march=native"));
        Assert.AreEqual("src/**/*.cpp",  EnvExpander.Expand("src/**/*.cpp"));
        Assert.AreEqual(string.Empty,    EnvExpander.Expand(string.Empty));
    }

    [TestMethod]
    public void Expand_KnownVar_ReplacedWithValue()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_VAR", "/usr/local");
        try
        {
            Assert.AreEqual("/usr/local/include", EnvExpander.Expand("${DOTORI_TEST_VAR}/include"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_VAR", null);
        }
    }

    [TestMethod]
    public void Expand_UnknownVar_ReplacedWithEmpty()
    {
        // Use a name that definitely won't be set
        var result = EnvExpander.Expand("prefix_${DOTORI_DEFINITELY_NOT_SET_XYZ123}_suffix");
        Assert.AreEqual("prefix__suffix", result);
    }

    [TestMethod]
    public void Expand_MultipleVars_AllReplaced()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_A", "hello");
        Environment.SetEnvironmentVariable("DOTORI_TEST_B", "world");
        try
        {
            Assert.AreEqual("hello world", EnvExpander.Expand("${DOTORI_TEST_A} ${DOTORI_TEST_B}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_A", null);
            Environment.SetEnvironmentVariable("DOTORI_TEST_B", null);
        }
    }

    [TestMethod]
    public void Expand_UnclosedBrace_TreatedAsLiteral()
    {
        // ${VAR without closing } should be kept as-is
        var result = EnvExpander.Expand("prefix${UNCLOSED");
        Assert.AreEqual("prefix${UNCLOSED", result);
    }

    [TestMethod]
    public void Expand_EmptyVarName_ReplacedWithEmpty()
    {
        // ${} with empty name
        var result = EnvExpander.Expand("a${}b");
        // Environment.GetEnvironmentVariable("") returns null → empty string
        Assert.AreEqual("ab", result);
    }

    [TestMethod]
    public void Expand_VarAtStart_Replaced()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_SDK_START", "/opt/sdk");
        try
        {
            Assert.AreEqual("/opt/sdk/lib", EnvExpander.Expand("${DOTORI_TEST_SDK_START}/lib"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_SDK_START", null);
        }
    }

    [TestMethod]
    public void Expand_VarAtEnd_Replaced()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_VER_END", "1.2.3");
        try
        {
            Assert.AreEqual("-DAPP_VERSION=1.2.3", EnvExpander.Expand("-DAPP_VERSION=${DOTORI_TEST_VER_END}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_VER_END", null);
        }
    }

    [TestMethod]
    public void ExpandNullable_Null_ReturnsNull()
    {
        Assert.IsNull(EnvExpander.ExpandNullable(null));
    }

    [TestMethod]
    public void ExpandNullable_NonNull_Expands()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_NV", "expanded");
        try
        {
            Assert.AreEqual("expanded", EnvExpander.ExpandNullable("${DOTORI_TEST_NV}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_NV", null);
        }
    }

    // ─── Integration: EnvExpander in ProjectFlattener ──────────────────────

    private static TargetContext LinuxDebug => new()
    {
        Platform = "linux", Config = "debug", Compiler = "clang", Runtime = "static",
    };

    private static FlatProjectModel FlattenSource(string source)
    {
        var file = DotoriParser.ParseSource(source, "<test>");
        return ProjectFlattener.Flatten(file.Project!, "<test>", LinuxDebug);
    }

    [TestMethod]
    public void Flatten_EnvVar_InDefines_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_VER", "42");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    defines { "APP_VERSION=${DOTORI_TEST_VER}" }
                }
                """);

            Assert.Contains("APP_VERSION=42", model.Defines);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_VER", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_InCompileFlags_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_SDK", "/opt/mysdk");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    compile-flags { "-I${DOTORI_TEST_SDK}/include" }
                }
                """);

            Assert.Contains("-I/opt/mysdk/include", model.CompileFlags);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_SDK", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_InLinkFlags_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_LINK_SDK", "/opt/mysdk");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    link-flags { "-L${DOTORI_TEST_LINK_SDK}/lib" }
                }
                """);

            Assert.Contains("-L/opt/mysdk/lib", model.LinkFlags);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_LINK_SDK", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_InHeaders_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_BOOST", "/usr/local/boost");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    headers {
                        public "${DOTORI_TEST_BOOST}/include/"
                    }
                }
                """);

            Assert.IsTrue(model.Headers.Any(h => h.Path == "/usr/local/boost/include/"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_BOOST", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_InSourceGlob_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_SRC", "src/platform");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    sources { include "${DOTORI_TEST_SRC}/**/*.cpp" }
                }
                """);

            Assert.IsTrue(model.Sources.Any(s => s.Glob == "src/platform/**/*.cpp"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_SRC", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_InPreBuild_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_SCRIPTS", "/opt/scripts");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    pre-build { "${DOTORI_TEST_SCRIPTS}/gen_version.sh" }
                }
                """);

            Assert.Contains("/opt/scripts/gen_version.sh", model.PreBuildCommands);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_SCRIPTS", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_InOutput_Expanded()
    {
        Environment.SetEnvironmentVariable("DOTORI_TEST_DIST", "/dist");
        try
        {
            var model = FlattenSource("""
                project MyApp {
                    type = executable
                    output {
                        binaries = "${DOTORI_TEST_DIST}/bin/"
                    }
                }
                """);

            Assert.IsNotNull(model.Output);
            Assert.AreEqual("/dist/bin/", model.Output.Binaries);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTORI_TEST_DIST", null);
        }
    }

    [TestMethod]
    public void Flatten_EnvVar_UnknownVar_ExpandsToEmpty()
    {
        var model = FlattenSource("""
            project MyApp {
                type = executable
                compile-flags { "-DFOO=${DOTORI_DEFINITELY_NOT_SET_XYZ999}" }
            }
            """);

        Assert.Contains("-DFOO=", model.CompileFlags);
    }
}
