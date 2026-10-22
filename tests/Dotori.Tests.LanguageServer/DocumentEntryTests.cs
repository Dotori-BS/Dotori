using Dotori.LanguageServer;

namespace Dotori.Tests.LanguageServer;

[TestClass]
public sealed class DocumentEntryTests
{
    [TestMethod]
    public void LocalPath_DecodesVsCodeWindowsDriveAndReservedCharacters()
    {
        var entry = new DocumentEntry("file:///c%3A/projects/My%20Project/%23config/.dotori", "", 1);
        var expected = OperatingSystem.IsWindows()
            ? @"c:\projects\My Project\#config\.dotori"
            : "/c:/projects/My Project/#config/.dotori";
        Assert.AreEqual(expected, entry.LocalPath);
    }

    [TestMethod]
    public void LocalPath_RoundTripsLocalFilesWithoutDoubleDecoding()
    {
        var path = Path.Combine(Path.GetTempPath(), "project space # %20 한글", ".dotori");
        var encodedPath = string.Join('/', path.Replace('\\', '/').Split('/')
            .Select(System.Uri.EscapeDataString));
        var uri = "file://" + (OperatingSystem.IsWindows() ? "/" : "") + encodedPath;
        Assert.AreEqual(path, new DocumentEntry(uri, "", 1).LocalPath);
    }

    [TestMethod]
    [DataRow("untitled:Untitled-1")]
    [DataRow("https://example.test/.dotori")]
    [DataRow("not a uri")]
    public void LocalPath_NonFileUrisHaveNoLocalPath(string uri)
    {
        Assert.IsNull(new DocumentEntry(uri, "", 1).LocalPath);
    }
}
