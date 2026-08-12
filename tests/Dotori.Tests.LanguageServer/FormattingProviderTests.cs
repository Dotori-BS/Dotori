using Dotori.Core.Parsing;
using Dotori.LanguageServer.Providers;

namespace Dotori.Tests.LanguageServer;

[TestClass]
public sealed class FormattingProviderTests
{
    [TestMethod]
    public void Format_UnformattedDocument_ReturnsFullDocumentEdit()
    {
        const string source = "project App { type=executable std=c++23 }";

        var edits = FormattingProvider.Format(source, "/project/.dotori");

        Assert.HasCount(1, edits);
        Assert.AreEqual(0, edits[0].Range.Start.Line);
        StringAssert.Contains(edits[0].NewText, "project App {");
        StringAssert.Contains(edits[0].NewText, "    type = executable");
    }

    [TestMethod]
    public void Format_AlreadyFormattedDocument_ReturnsNoEdits()
    {
        var formatted = DotoriFormatter.Format(
            DotoriParser.ParseSource("project App { type = executable }"));

        var edits = FormattingProvider.Format(formatted, "/project/.dotori");

        Assert.IsEmpty(edits);
    }

    [TestMethod]
    public void Format_InvalidDocument_ReturnsNoEdits()
    {
        var edits = FormattingProvider.Format("project App {", "/project/.dotori");

        Assert.IsEmpty(edits);
    }
}
