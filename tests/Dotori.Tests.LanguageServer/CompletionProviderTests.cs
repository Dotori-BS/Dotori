using Dotori.LanguageServer.Providers;

namespace Dotori.Tests.LanguageServer;

[TestClass]
public class CompletionProviderTests
{
    [TestMethod]
    public void GetCompletions_EmptyFile_SuggestsTopLevel()
    {
        var list = CompletionProvider.GetCompletions("", 0, 0);

        Assert.IsNotNull(list);
        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "project");
        CollectionAssert.Contains(labels, "package");
    }

    [TestMethod]
    public void GetCompletions_InsideProjectBlock_SuggestsProjectKeywords()
    {
        const string text = """
            project MyApp {

            }
            """;
        // Line 1, inside the project block
        var list = CompletionProvider.GetCompletions(text, 1, 4);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "type");
        CollectionAssert.Contains(labels, "std");
        CollectionAssert.Contains(labels, "sources");
        CollectionAssert.Contains(labels, "dependencies");
    }

    [TestMethod]
    public void GetCompletions_InsidePackageBlock_SuggestsPackageKeywords()
    {
        const string text = """
            package {

            }
            """;
        var list = CompletionProvider.GetCompletions(text, 1, 4);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "name");
        CollectionAssert.Contains(labels, "version");
        CollectionAssert.Contains(labels, "license");
    }

    [TestMethod]
    public void GetCompletions_AfterTypeEquals_SuggestsProjectTypes()
    {
        const string text = """
            project MyApp {
                type =
            }
            """;
        // Position after "type = " on line 1
        var list = CompletionProvider.GetCompletions(text, 1, 12);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "executable");
        CollectionAssert.Contains(labels, "test");
        CollectionAssert.Contains(labels, "static-library");
        CollectionAssert.Contains(labels, "shared-library");
        CollectionAssert.Contains(labels, "header-only");
    }

    [TestMethod]
    public void GetCompletions_AfterStdEquals_SuggestsCxxStandards()
    {
        const string text = """
            project MyApp {
                std =
            }
            """;
        var list = CompletionProvider.GetCompletions(text, 1, 11);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "c++17");
        CollectionAssert.Contains(labels, "c++20");
        CollectionAssert.Contains(labels, "c++23");
    }

    [TestMethod]
    public void GetCompletions_AfterOptimizeEquals_SuggestsLevels()
    {
        const string text = """
            project MyApp {
                optimize =
            }
            """;
        var list = CompletionProvider.GetCompletions(text, 1, 16);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "none");
        CollectionAssert.Contains(labels, "size");
        CollectionAssert.Contains(labels, "speed");
        CollectionAssert.Contains(labels, "full");
    }

    [TestMethod]
    public void GetCompletions_InsideConditionBracket_SuggestsConditionAtoms()
    {
        const string text = """
            project MyApp {
                [
            }
            """;
        // Position after "[" on line 1
        var list = CompletionProvider.GetCompletions(text, 1, 5);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "windows");
        CollectionAssert.Contains(labels, "linux");
        CollectionAssert.Contains(labels, "macos");
        CollectionAssert.Contains(labels, "debug");
        CollectionAssert.Contains(labels, "release");
        CollectionAssert.Contains(labels, "msvc");
        CollectionAssert.Contains(labels, "clang");
    }

    [TestMethod]
    public void GetCompletions_AfterRuntimeLinkEquals_SuggestsLinkModes()
    {
        const string text = """
            project MyApp {
                runtime-link =
            }
            """;
        var list = CompletionProvider.GetCompletions(text, 1, 20);

        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "static");
        CollectionAssert.Contains(labels, "dynamic");
    }

    [TestMethod]
    public void GetCompletions_ResultNotNull_NeverThrows()
    {
        // Stress: random positions should not throw
        const string text = "project Foo { type = executable }";
        for (int line = 0; line <= 1; line++)
        {
            for (int ch = 0; ch <= text.Length + 2; ch++)
            {
                var result = CompletionProvider.GetCompletions(text, line, ch);
                Assert.IsNotNull(result);
            }
        }
    }

    [TestMethod]
    public void GetCompletions_InsideProjectBlock_SuggestsOptionKeyword()
    {
        const string text = """
            project MyApp {

            }
            """;
        var list = CompletionProvider.GetCompletions(text, 1, 4);
        var labels = list.Items.Select(i => i.Label).ToList();
        CollectionAssert.Contains(labels, "option");
    }
}
