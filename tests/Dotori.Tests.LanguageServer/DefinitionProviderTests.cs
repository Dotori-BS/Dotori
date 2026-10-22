using Dotori.LanguageServer.Providers;

namespace Dotori.Tests.LanguageServer;

[TestClass]
public sealed class DefinitionProviderTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"dotori-definition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_directory, "lib"));
        File.WriteAllText(Path.Combine(_directory, "lib", ".dotori"), "project Lib {}");
    }

    [TestCleanup]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void GetDefinition_NestedDependencyPath_ResolvesDotoriFile()
    {
        const string text = """
            project App {
                dependencies {
                    lib = {
                        path = "./lib"
                    }
                }
            }
            """;

        var location = DefinitionProvider.GetDefinition(text, 3, 23, Path.Combine(_directory, ".dotori"));

        Assert.IsNotNull(location);
        Assert.AreEqual(new Uri(Path.Combine(_directory, "lib", ".dotori")).AbsoluteUri, location.Uri);
    }

    [TestMethod]
    public void GetDefinition_InlineDependencyWithCommentBraces_ResolvesPath()
    {
        const string text = "project App { dependencies { # } ignored\n lib = { path = \"./lib\" } } }";

        var location = DefinitionProvider.GetDefinition(text, 1, 18, Path.Combine(_directory, ".dotori"));

        Assert.IsNotNull(location);
    }

    [TestMethod]
    public void GetDefinition_PathOutsideDependencies_ReturnsNull()
    {
        const string text = "project App { output { path = \"./lib\" } }";

        Assert.IsNull(DefinitionProvider.GetDefinition(text, 0, 32, Path.Combine(_directory, ".dotori")));
    }

    [TestMethod]
    public void GetDefinition_PositionBeyondLine_ReturnsNull()
    {
        const string text = "path = \"./lib\"";

        Assert.IsNull(DefinitionProvider.GetDefinition(text, 0, 200, Path.Combine(_directory, ".dotori")));
        Assert.IsNull(DefinitionProvider.GetDefinition(text, -1, 0, Path.Combine(_directory, ".dotori")));
    }
}
