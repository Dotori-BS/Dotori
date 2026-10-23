using Dotori.Core.Parsing;

namespace Dotori.Tests.Parsing;

[TestClass]
public sealed class LexerTests
{
    [TestMethod]
    public void Lexer_BasicTokens()
    {
        var tokens = new Lexer("project MyApp { }", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.Ident,   tokens[0].Kind);
        Assert.AreEqual("project",          tokens[0].Text);
        Assert.AreEqual(TokenKind.Ident,   tokens[1].Kind);
        Assert.AreEqual("MyApp",            tokens[1].Text);
        Assert.AreEqual(TokenKind.LBrace,  tokens[2].Kind);
        Assert.AreEqual(TokenKind.RBrace,  tokens[3].Kind);
        Assert.AreEqual(TokenKind.Eof,     tokens[4].Kind);
    }

    [TestMethod]
    public void Lexer_StringLiteral()
    {
        var tokens = new Lexer("\"hello world\"", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.String, tokens[0].Kind);
        Assert.AreEqual("hello world",    tokens[0].Text);
    }

    [TestMethod]
    public void Lexer_BlockComment_EmitsCommentToken()
    {
        var tokens = new Lexer("(* this is a comment *) project", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.Comment, tokens[0].Kind);
        Assert.AreEqual("this is a comment", tokens[0].Text);
        Assert.AreEqual(TokenKind.Ident, tokens[1].Kind);
        Assert.AreEqual("project",        tokens[1].Text);
    }

    [TestMethod]
    public void Lexer_NestedBlockComment_EmitsCommentToken()
    {
        var tokens = new Lexer("(* outer (* inner *) *) foo", "<test>").Tokenize();
        // nested (* inner *) is treated as part of the content; outer comment text = "outer (* inner *)"
        Assert.AreEqual(TokenKind.Comment, tokens[0].Kind);
        Assert.AreEqual(TokenKind.Ident, tokens[1].Kind);
        Assert.AreEqual("foo", tokens[1].Text);
    }

    [TestMethod]
    public void Lexer_LineComment_Hash_EmitsCommentToken()
    {
        var tokens = new Lexer("# this is a line comment\nproject", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.Comment, tokens[0].Kind);
        Assert.AreEqual("this is a line comment", tokens[0].Text);
        Assert.AreEqual(TokenKind.Ident, tokens[1].Kind);
    }

    [TestMethod]
    public void Lexer_BoolLiterals()
    {
        var tokens = new Lexer("true false", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.BoolTrue,  tokens[0].Kind);
        Assert.AreEqual(TokenKind.BoolFalse, tokens[1].Kind);
    }

    [TestMethod]
    public void Lexer_HyphenatedIdent()
    {
        var tokens = new Lexer("static-library", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.Ident,  tokens[0].Kind);
        Assert.AreEqual("static-library", tokens[0].Text);
    }

    [TestMethod]
    public void Lexer_CxxStdIdent()
    {
        var tokens = new Lexer("c++23", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.Ident, tokens[0].Kind);
        Assert.AreEqual("c++23",          tokens[0].Text);
    }

    [TestMethod]
    public void Lexer_Integer()
    {
        var tokens = new Lexer("26", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.Integer, tokens[0].Kind);
        Assert.AreEqual("26",              tokens[0].Text);
    }

    [TestMethod]
    public void Lexer_ConditionTokens()
    {
        var tokens = new Lexer("[windows.release]", "<test>").Tokenize();
        Assert.AreEqual(TokenKind.LBracket, tokens[0].Kind);
        Assert.AreEqual(TokenKind.Ident,    tokens[1].Kind);
        Assert.AreEqual("windows",           tokens[1].Text);
        Assert.AreEqual(TokenKind.Dot,      tokens[2].Kind);
        Assert.AreEqual(TokenKind.Ident,    tokens[3].Kind);
        Assert.AreEqual("release",           tokens[3].Text);
        Assert.AreEqual(TokenKind.RBracket, tokens[4].Kind);
    }

    [TestMethod]
    public void Lexer_SourceLocation_Tracked()
    {
        var tokens = new Lexer("foo\nbar", "<test>").Tokenize();
        Assert.AreEqual(1, tokens[0].Location.Line);
        Assert.AreEqual(1, tokens[0].Location.Column);
        Assert.AreEqual(2, tokens[1].Location.Line);
        Assert.AreEqual(1, tokens[1].Location.Column);
    }
}

[TestClass]
public sealed class ParserTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [TestMethod]
    public void Parser_SimpleApp_ProjectName()
    {
        var file = DotoriParser.ParseFile(FixturePath("simple-app.dotori"));
        Assert.IsNotNull(file.Project);
        Assert.AreEqual("MyApp", file.Project.Name);
        Assert.IsNull(file.Package);
    }

    [TestMethod]
    public void Parser_SimpleApp_ProjectType()
    {
        var file = DotoriParser.ParseFile(FixturePath("simple-app.dotori"));
        var typeProp = file.Project!.Items.OfType<ProjectTypeProp>().Single();
        Assert.AreEqual(ProjectType.Executable, typeProp.Value);
    }

    [TestMethod]
    public void Parser_TestProjectType()
    {
        var file = DotoriParser.ParseSource(
            "project UnitTests { type = test }", "<test>");
        var typeProp = file.Project!.Items.OfType<ProjectTypeProp>().Single();

        Assert.AreEqual(ProjectType.Test, typeProp.Value);
    }

    [TestMethod]
    public void Parser_SimpleApp_CxxStd()
    {
        var file = DotoriParser.ParseFile(FixturePath("simple-app.dotori"));
        var stdProp = file.Project!.Items.OfType<StdProp>().Single();
        Assert.AreEqual(CxxStd.Cxx23, stdProp.Value);
    }

    [TestMethod]
    public void Parser_SimpleApp_Sources()
    {
        var file = DotoriParser.ParseFile(FixturePath("simple-app.dotori"));
        var sources = file.Project!.Items.OfType<SourcesBlock>().Single();
        Assert.IsFalse(sources.IsModules);
        Assert.HasCount(1, sources.Items);
        Assert.AreEqual("src/**/*.cpp", sources.Items[0].Glob);
        Assert.IsTrue(sources.Items[0].IsInclude);
    }

    [TestMethod]
    public void Parser_SimpleApp_Dependencies()
    {
        var file = DotoriParser.ParseFile(FixturePath("simple-app.dotori"));
        var deps = file.Project!.Items.OfType<DependenciesBlock>().Single();
        Assert.HasCount(2, deps.Items);

        var myLib = deps.Items.First(d => d.Name == "my-lib");
        Assert.IsInstanceOfType<ComplexDependency>(myLib.Value);
        Assert.AreEqual("../lib", ((ComplexDependency)myLib.Value).Path);

        var fmt = deps.Items.First(d => d.Name == "fmt");
        Assert.IsInstanceOfType<VersionDependency>(fmt.Value);
        Assert.AreEqual("10.2.0", ((VersionDependency)fmt.Value).Version);
    }

    [TestMethod]
    public void Parser_SimpleApp_ConditionBlocks()
    {
        var file = DotoriParser.ParseFile(FixturePath("simple-app.dotori"));
        var conditions = file.Project!.Items.OfType<ConditionBlock>().ToList();
        Assert.HasCount(2, conditions);
        Assert.AreEqual("debug",   conditions[0].Condition.ToString());
        Assert.AreEqual("release", conditions[1].Condition.ToString());
    }

    [TestMethod]
    public void Parser_LibWithPackage_HasBoth()
    {
        var file = DotoriParser.ParseFile(FixturePath("lib-with-package.dotori"));
        Assert.IsNotNull(file.Project);
        Assert.IsNotNull(file.Package);
        Assert.AreEqual("MyLib",   file.Project.Name);
        Assert.AreEqual("my-lib",  file.Package.Name);
        Assert.AreEqual("1.0.0",   file.Package.Version);
        Assert.AreEqual("MIT",     file.Package.License);
        Assert.HasCount(2, file.Package.Authors);
    }

    [TestMethod]
    public void Parser_LibWithPackage_ModulesBlock()
    {
        var file = DotoriParser.ParseFile(FixturePath("lib-with-package.dotori"));
        var modules = file.Project!.Items.OfType<SourcesBlock>().Single(b => b.IsModules);
        Assert.AreEqual("src/**/*.cppm", modules.Items[0].Glob);
    }

    [TestMethod]
    public void Parser_ComplexConditions_NestedAtoms()
    {
        var file = DotoriParser.ParseFile(FixturePath("complex-conditions.dotori"));
        var conditions = file.Project!.Items.OfType<ConditionBlock>().ToList();
        var windowsRelease = conditions.First(c => c.Condition.ToString() == "windows.release");
        Assert.AreEqual(2, windowsRelease.Condition.Specificity);
    }

    [TestMethod]
    public void Parser_ComplexConditions_UnityBuild()
    {
        var file = DotoriParser.ParseFile(FixturePath("complex-conditions.dotori"));
        var unity = file.Project!.Items.OfType<UnityBuildBlock>().Single();
        Assert.IsTrue(unity.Enabled);
        Assert.AreEqual(8, unity.BatchSize);
        Assert.HasCount(1, unity.Exclude);
        Assert.AreEqual("src/main.cpp", unity.Exclude[0]);
    }

    [TestMethod]
    public void Parser_ComplexConditions_Pch()
    {
        var file = DotoriParser.ParseFile(FixturePath("complex-conditions.dotori"));
        var pch = file.Project!.Items.OfType<PchBlock>().Single();
        Assert.AreEqual("src/pch.h",   pch.Header);
        Assert.AreEqual("src/pch.cpp", pch.Source);
    }

    [TestMethod]
    public void Parser_ComplexConditions_AndroidApiLevel()
    {
        var file = DotoriParser.ParseFile(FixturePath("complex-conditions.dotori"));
        var android = file.Project!.Items.OfType<ConditionBlock>()
            .First(c => c.Condition.ToString() == "android");
        var apiLevel = android.Items.OfType<AndroidApiLevelProp>().Single();
        Assert.AreEqual(26, apiLevel.Value);
    }

    [TestMethod]
    public void Parser_Error_UnknownKeyword_Throws()
    {
        Assert.ThrowsExactly<ParseException>(() =>
            DotoriParser.ParseSource("unknown { }", "<test>"));
    }

    [TestMethod]
    public void Parser_Error_DuplicateProject_Throws()
    {
        Assert.ThrowsExactly<ParseException>(() =>
            DotoriParser.ParseSource(
                "project A { } project B { }", "<test>"));
    }

    [TestMethod]
    public void Parser_EmscriptenFlags()
    {
        var file = DotoriParser.ParseFile(FixturePath("complex-conditions.dotori"));
        var wasmEmscripten = file.Project!.Items.OfType<ConditionBlock>()
            .First(c => c.Condition.ToString() == "wasm.emscripten");
        var flags = wasmEmscripten.Items.OfType<EmscriptenFlagsProp>().Single();
        Assert.HasCount(2, flags.Flags);
        Assert.AreEqual("-sUSE_SDL=2", flags.Flags[0]);
    }

    // ─── Phase 1-J: output / pre-build / post-build / modules export-map ──────

    [TestMethod]
    public void Parser_OutputBlock_Parsed()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                output {
                    binaries  = "bin/"
                    libraries = "lib/"
                    symbols   = "pdb/"
                }
            }
            """, "<test>");
        var block = file.Project!.Items.OfType<OutputBlock>().Single();
        Assert.AreEqual("bin/",  block.Binaries);
        Assert.AreEqual("lib/",  block.Libraries);
        Assert.AreEqual("pdb/",  block.Symbols);
    }

    [TestMethod]
    public void Parser_OutputBlock_PartialFields()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                output {
                    binaries = "dist/"
                }
            }
            """, "<test>");
        var block = file.Project!.Items.OfType<OutputBlock>().Single();
        Assert.AreEqual("dist/", block.Binaries);
        Assert.IsNull(block.Libraries);
        Assert.IsNull(block.Symbols);
    }

    [TestMethod]
    public void Parser_PreBuildBlock_Parsed()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                pre-build {
                    "scripts/gen_version.sh"
                    "scripts/download_assets.sh"
                }
            }
            """, "<test>");
        var block = file.Project!.Items.OfType<PreBuildBlock>().Single();
        Assert.HasCount(2, block.Commands);
        Assert.AreEqual("scripts/gen_version.sh",     block.Commands[0]);
        Assert.AreEqual("scripts/download_assets.sh", block.Commands[1]);
    }

    [TestMethod]
    public void Parser_PostBuildBlock_Parsed()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                post-build {
                    "scripts/sign.sh"
                }
            }
            """, "<test>");
        var block = file.Project!.Items.OfType<PostBuildBlock>().Single();
        Assert.HasCount(1, block.Commands);
        Assert.AreEqual("scripts/sign.sh", block.Commands[0]);
    }

    [TestMethod]
    public void Parser_ModulesExportMap_Parsed()
    {
        var file = DotoriParser.ParseSource("""
            project MyLib {
                type = static-library
                modules {
                    include "src/**/*.cppm"
                    export-map = false
                }
            }
            """, "<test>");
        var modules = file.Project!.Items.OfType<SourcesBlock>().Single(b => b.IsModules);
        Assert.IsFalse(modules.ExportMap);
    }

    [TestMethod]
    public void Parser_ModulesExportMap_DefaultNull()
    {
        var file = DotoriParser.ParseSource("""
            project MyLib {
                type = static-library
                modules {
                    include "src/**/*.cppm"
                }
            }
            """, "<test>");
        var modules = file.Project!.Items.OfType<SourcesBlock>().Single(b => b.IsModules);
        Assert.IsNull(modules.ExportMap);
    }

    [TestMethod]
    public void Parser_OutputBlock_InCondition()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                [release] {
                    output {
                        binaries = "dist/"
                    }
                }
            }
            """, "<test>");
        var cond = file.Project!.Items.OfType<ConditionBlock>()
            .First(c => c.Condition.ToString() == "release");
        var block = cond.Items.OfType<OutputBlock>().Single();
        Assert.AreEqual("dist/", block.Binaries);
    }

    [TestMethod]
    public void Parser_ForceCxx_True()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                c-as-cpp = true
            }
            """, "<test>");
        var prop = file.Project!.Items.OfType<ForceCxxProp>().Single();
        Assert.IsTrue(prop.Value);
    }

    [TestMethod]
    public void Parser_ForceCxx_False()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                type = executable
                c-as-cpp = false
            }
            """, "<test>");
        var prop = file.Project!.Items.OfType<ForceCxxProp>().Single();
        Assert.IsFalse(prop.Value);
    }

    [TestMethod]
    public void Parser_ObjectiveCBooleanProperties_AreParsed()
    {
        var file = DotoriParser.ParseSource("""
            project MyApp {
                objc-arc = true
                objc-as-objcpp = false
            }
            """, "<test>");

        Assert.IsTrue(file.Project!.Items.OfType<ObjcArcProp>().Single().Value);
        Assert.IsFalse(file.Project.Items.OfType<ForceObjcppProp>().Single().Value);
    }
}
