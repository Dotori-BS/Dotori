using Dotori.LanguageServer.Providers;

namespace Dotori.Tests.LanguageServer;

[TestClass]
public sealed class OptionSymbolProviderTests
{
    private const string Source = """
        project App {
            option simd {
                default = false
            }
            [simd] {
                defines { "USE_SIMD" }
            }
            dependencies {
                math = { version = "1.0.0", option = { "simd" "logging" } }
            }
        }
        """;

    [TestMethod]
    public void GetReferences_DeclaredOption_FindsConditionsAndDependencyOptions()
    {
        var locations = OptionSymbolProvider.GetReferences(
            Source, "file:///project/.dotori", 1, 12, includeDeclaration: true);

        Assert.HasCount(3, locations);
        Assert.AreEqual(1, locations[0].Range.Start.Line);
        Assert.AreEqual(4, locations[1].Range.Start.Line);
        Assert.AreEqual(8, locations[2].Range.Start.Line);
    }

    [TestMethod]
    public void GetReferences_WithoutDeclaration_ReturnsUsesOnly()
    {
        var locations = OptionSymbolProvider.GetReferences(
            Source, "file:///project/.dotori", 4, 6, includeDeclaration: false);

        Assert.HasCount(2, locations);
        Assert.IsTrue(locations.All(location => location.Range.Start.Line != 1));
    }

    [TestMethod]
    public void Rename_DeclaredOption_ProducesEditsForEveryOccurrence()
    {
        var edit = OptionSymbolProvider.Rename(
            Source, "file:///project/.dotori", 1, 12, "vectorized");

        Assert.IsNotNull(edit);
        Assert.HasCount(3, edit.Changes["file:///project/.dotori"]);
        Assert.IsTrue(edit.Changes["file:///project/.dotori"]
            .All(item => item.NewText == "vectorized"));
    }

    [TestMethod]
    public void Rename_KeywordOrInvalidName_IsRejected()
    {
        Assert.IsNull(OptionSymbolProvider.Rename(
            Source, "file:///project/.dotori", 0, 2, "renamed"));
        Assert.IsNull(OptionSymbolProvider.Rename(
            Source, "file:///project/.dotori", 1, 12, "invalid name"));
    }

    [TestMethod]
    public void Rename_DoesNotEditCommentsThatLookLikeOptionUses()
    {
        const string source = """
            project App {
                option simd {
                    default = false
                }
                # [simd] and option = "simd" are documentation
                [simd] {
                    defines { "USE_SIMD" }
                }
            }
            """;

        var edit = OptionSymbolProvider.Rename(
            source, "file:///project/.dotori", 1, 12, "vectorized");

        Assert.IsNotNull(edit);
        Assert.HasCount(2, edit.Changes["file:///project/.dotori"]);
    }
}
