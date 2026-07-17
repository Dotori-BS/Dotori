using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Tests.Parsing;

[TestClass]
public sealed class BuildRuleTests
{
    [TestMethod]
    public void Rule_ParsesFlattensAndFormats()
    {
        var source = """
            project Generated {
                rule "schema" {
                    command = "tool schema.json generated/schema.hpp"
                    inputs { "schema.json" "templates/**/*.txt" }
                    outputs { "generated/schema.hpp" }
                }
            }
            """;
        var file = DotoriParser.ParseSource(source);
        var rule = file.Project!.Items.OfType<BuildRuleBlock>().Single();

        Assert.AreEqual("schema", rule.Name);
        Assert.AreEqual(2, rule.Inputs.Count);
        Assert.AreEqual("generated/schema.hpp", rule.Outputs.Single());

        var context = new TargetContext
        {
            Platform = "linux", Config = "debug", Compiler = "clang", Runtime = "static",
        };
        var model = ProjectFlattener.Flatten(file.Project, "/tmp/project.dotori", context);
        Assert.AreEqual("tool schema.json generated/schema.hpp", model.BuildRules.Single().Command);

        var formatted = DotoriFormatter.Format(file);
        var reparsed = DotoriParser.ParseSource(formatted);
        Assert.AreEqual("schema", reparsed.Project!.Items.OfType<BuildRuleBlock>().Single().Name);
    }

    [TestMethod]
    public void Rule_RequiresCommandAndOutput()
    {
        Assert.Throws<ParseException>(() => DotoriParser.ParseSource("""
            project Bad { rule "missing" { inputs { "a" } } }
            """));
        Assert.Throws<ParseException>(() => DotoriParser.ParseSource("""
            project Bad { rule "missing" { command = "true" } }
            """));
    }
}
