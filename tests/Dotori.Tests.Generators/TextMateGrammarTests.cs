using System.Text.Json;
using System.Text.RegularExpressions;
using Dotori.Core.Grammar;

namespace Dotori.Tests.Generators;

[TestClass]
public class TextMateGrammarTests
{
    [TestMethod]
    public void GeneratedValuePatterns_MatchCompleteDslTokens()
    {
        using var json = JsonDocument.Parse(new TextMateGrammarGenerator().Generate());
        var patterns = json.RootElement.GetProperty("repository")
            .GetProperty("value-keywords").GetProperty("patterns");
        string[][] values = [DotoriGrammarDefinition.TypeValues,
            DotoriGrammarDefinition.StdValues, DotoriGrammarDefinition.EnumValues];
        for (var i = 0; i < values.Length; i++)
        {
            var regex = new Regex(patterns[i].GetProperty("match").GetString()!);
            foreach (var value in values[i])
            {
                var match = regex.Match(value);
                Assert.IsTrue(match.Success, value);
                Assert.AreEqual(value, match.Value, value);
                Assert.IsFalse(regex.IsMatch(value + "-extra"), value);
                Assert.IsFalse(regex.IsMatch("prefix-" + value), value);
            }
        }
    }

    [TestMethod]
    public void GeneratedIdentifierPattern_IncludesPlusAndHyphen()
    {
        using var json = JsonDocument.Parse(new TextMateGrammarGenerator().Generate());
        var pattern = json.RootElement.GetProperty("repository").GetProperty("identifiers")
            .GetProperty("match").GetString()!;
        foreach (var identifier in new[] { "c++23-extra", "project-helper", "libc++" })
            Assert.AreEqual(identifier, Regex.Match(identifier, pattern).Value);
    }
}
