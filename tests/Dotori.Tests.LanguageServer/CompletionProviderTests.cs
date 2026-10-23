using Dotori.LanguageServer.Providers;
using Dotori.LanguageServer.Protocol;

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

    [TestMethod]
    public void GetCompletions_AfterBooleanProperties_SuggestsOnlyBooleanValues()
    {
        var contexts = new[]
        {
            "project Demo { lto = ",
            "project Demo { warnings-as-errors = ",
            "project Demo { c-as-cpp = ",
            "project Demo { option feature { default = ",
            "project Demo { modules { export-map = ",
            "project Demo { pch { modules = ",
            "project Demo { unity-build { enabled = ",
        };

        foreach (var text in contexts)
        {
            var list = CompletionProvider.GetCompletions(text, 0, text.Length);
            CollectionAssert.AreEqual(
                new[] { "true", "false" },
                list.Items.Select(item => item.Label).ToArray(),
                $"Expected only boolean values for: {text}");
        }
    }

    [TestMethod]
    public void GetCompletions_BooleanPropertyOutsideItsValidBlock_DoesNotSuggestBooleans()
    {
        const string text = "project Demo { modules = ";

        var list = CompletionProvider.GetCompletions(text, 0, text.Length);

        Assert.AreEqual(0, list.Items.Count);
    }

    [TestMethod]
    public void GetCompletions_InsideNestedBlocks_SuggestsBlockSpecificKeywords()
    {
        const string text = "project Demo { sources { ";

        var list = CompletionProvider.GetCompletions(text, 0, text.Length);
        var labels = list.Items.Select(item => item.Label).ToArray();

        CollectionAssert.AreEqual(new[] { "include", "exclude" }, labels);
    }

    [TestMethod]
    public void GetCompletions_AfterEnumProperty_SuggestsOnlyThatPropertyValues()
    {
        const string text = "project Demo { type = ";

        var list = CompletionProvider.GetCompletions(text, 0, text.Length);

        CollectionAssert.AreEqual(
            new[] { "executable", "test", "static-library", "shared-library", "header-only" },
            list.Items.Select(item => item.Label).ToArray());
    }

    [TestMethod]
    public void GetCompletions_AfterUnconstrainedValue_DoesNotSuggestBlockKeywords()
    {
        const string text = "project Demo { description = ";

        var list = CompletionProvider.GetCompletions(text, 0, text.Length);

        Assert.AreEqual(0, list.Items.Count);
    }

    [TestMethod]
    public void GetCompletions_OnNextLine_DoesNotReusePreviousAssignmentContext()
    {
        const string text = """
            project Demo {
                type = executable

            }
            """;

        var list = CompletionProvider.GetCompletions(text, 2, 0);
        var labels = list.Items.Select(item => item.Label).ToArray();

        CollectionAssert.Contains(labels, "lto");
        CollectionAssert.DoesNotContain(labels, "executable");
    }

    [TestMethod]
    public void GetCompletions_InPathString_SuggestsMatchingFilesAndDirectories()
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), $"dotori-completion-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(projectDirectory, "src");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "app"));
        File.WriteAllText(Path.Combine(sourceDirectory, "main.cpp"), string.Empty);
        File.WriteAllText(Path.Combine(sourceDirectory, "other.txt"), string.Empty);

        try
        {
            var documentPath = Path.Combine(projectDirectory, "project.dotori");
            const string text = "project Demo { sources { include \"src/";

            var list = CompletionProvider.GetCompletions(text, 0, text.Length, documentPath);
            var items = list.Items.ToDictionary(item => item.Label);

            Assert.IsTrue(list.IsIncomplete);
            Assert.IsTrue(items.ContainsKey("src/app/"));
            Assert.AreEqual(19, items["src/app/"].Kind);
            Assert.IsTrue(items.ContainsKey("src/main.cpp"));
            Assert.AreEqual(17, items["src/main.cpp"].Kind);
            Assert.IsTrue(items.ContainsKey("src/other.txt"));

            var fileEdit = items["src/main.cpp"].TextEdit;
            Assert.IsNotNull(fileEdit);
            Assert.AreEqual(text.IndexOf('"') + 1, fileEdit.Range.Start.Character);
            Assert.AreEqual(text.Length, fileEdit.Range.End.Character);
            Assert.AreEqual("src/main.cpp", fileEdit.NewText);
            var serialized = LspSerializer.Serialize(list);
            StringAssert.Contains(serialized, "\"textEdit\"");
            StringAssert.Contains(serialized, "\"newText\":\"src/main.cpp\"");

            const string partialText = "project Demo { sources { include \"src/ma";
            var filtered = CompletionProvider.GetCompletions(partialText, 0, partialText.Length, documentPath);
            CollectionAssert.AreEqual(new[] { "src/main.cpp" }, filtered.Items.Select(item => item.Label).ToArray());

            var pathContexts = new[]
            {
                "project Demo { manifest = \"src/ma",
                "project Demo { pch { header = \"src/ma",
                "project Demo { dependencies { lib = { path = \"src/ma",
                "project Demo { copy { from \"src/ma",
                "project Demo { resources { \"src/ma",
                "project Demo { unity-build { exclude { \"src/ma",
                "project Demo { output { binaries = \"src/ma",
            };
            foreach (var pathContext in pathContexts)
            {
                var contextItems = CompletionProvider.GetCompletions(
                    pathContext, 0, pathContext.Length, documentPath).Items;
                CollectionAssert.Contains(
                    contextItems.Select(item => item.Label).ToArray(),
                    "src/main.cpp",
                    $"Expected path candidates for: {pathContext}");
            }
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void GetCompletions_InNonPathString_DoesNotSuggestFilesystemEntries()
    {
        const string text = "project Demo { description = \"src/";

        var list = CompletionProvider.GetCompletions(text, 0, text.Length, Path.Combine(Path.GetTempPath(), "project.dotori"));

        Assert.AreEqual(0, list.Items.Count);
    }
}
