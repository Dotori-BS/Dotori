using Dotori.Core.Build;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class IncludeScannerTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateFile(string relativePath, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_tempDir, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ─── ExtractInclude ───────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractInclude_LocalQuote_Parsed()
    {
        var (path, isSystem) = IncludeScanner.ExtractInclude("#include \"mylib.h\"");
        Assert.AreEqual("mylib.h", path);
        Assert.IsFalse(isSystem);
    }

    [TestMethod]
    public void ExtractInclude_SystemAngle_Parsed()
    {
        var (path, isSystem) = IncludeScanner.ExtractInclude("#include <stdio.h>");
        Assert.AreEqual("stdio.h", path);
        Assert.IsTrue(isSystem);
    }

    [TestMethod]
    public void ExtractInclude_WithLeadingSpaces_Parsed()
    {
        var (path, isSystem) = IncludeScanner.ExtractInclude("  #  include  \"foo/bar.h\"");
        Assert.AreEqual("foo/bar.h", path);
        Assert.IsFalse(isSystem);
    }

    [TestMethod]
    public void ExtractInclude_NotInclude_ReturnsNull()
    {
        var (path, _) = IncludeScanner.ExtractInclude("// #include \"ignored.h\"");
        Assert.IsNull(path);
    }

    [TestMethod]
    public void ExtractInclude_EmptyLine_ReturnsNull()
    {
        var (path, _) = IncludeScanner.ExtractInclude("");
        Assert.IsNull(path);
    }

    [TestMethod]
    public void ExtractInclude_CodeLine_ReturnsNull()
    {
        var (path, _) = IncludeScanner.ExtractInclude("int x = 0;");
        Assert.IsNull(path);
    }

    // ─── ParseIncludes ────────────────────────────────────────────────────────

    [TestMethod]
    public void ParseIncludes_MixedIncludes_ReturnsBoth()
    {
        var file = CreateFile("main.cpp", """
            #include <stdio.h>
            #include "mylib.h"
            int main() {}
            """);

        var result = IncludeScanner.ParseIncludes(file);

        Assert.HasCount(2, result);
        Assert.AreEqual("stdio.h",  result[0].Path);
        Assert.IsTrue(result[0].IsSystem);
        Assert.AreEqual("mylib.h", result[1].Path);
        Assert.IsFalse(result[1].IsSystem);
    }

    [TestMethod]
    public void ParseIncludes_NoIncludes_ReturnsEmpty()
    {
        var file = CreateFile("empty.cpp", "int main() { return 0; }");
        var result = IncludeScanner.ParseIncludes(file);
        Assert.HasCount(0, result);
    }

    [TestMethod]
    public void ParseIncludes_NonExistentFile_ReturnsEmpty()
    {
        var result = IncludeScanner.ParseIncludes(Path.Combine(_tempDir, "nonexistent.cpp"));
        Assert.HasCount(0, result);
    }

    // ─── ResolveHeader ────────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveHeader_LocalInSameDir_Resolved()
    {
        var header = CreateFile("mylib.h", "");
        var sourceDir = _tempDir;

        var resolved = IncludeScanner.ResolveHeader("mylib.h", isSystem: false, sourceDir, []);
        Assert.AreEqual(header, resolved);
    }

    [TestMethod]
    public void ResolveHeader_InSearchPath_Resolved()
    {
        var includeDir = Path.Combine(_tempDir, "include");
        Directory.CreateDirectory(includeDir);
        var header = CreateFile("include/types.h", "");

        var resolved = IncludeScanner.ResolveHeader("types.h", isSystem: false, _tempDir, [includeDir]);
        Assert.AreEqual(header, resolved);
    }

    [TestMethod]
    public void ResolveHeader_NotFound_ReturnsNull()
    {
        var resolved = IncludeScanner.ResolveHeader("nonexistent.h", isSystem: false, _tempDir, []);
        Assert.IsNull(resolved);
    }

    // ─── BuildTree ────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildTree_SingleLevel_ChildResolved()
    {
        CreateFile("mylib.h", "// no includes");
        var main = CreateFile("main.cpp", "#include \"mylib.h\"");

        var tree = IncludeScanner.BuildTree(main, [], includeSystem: true);

        Assert.IsTrue(tree.IsResolved);
        Assert.HasCount(1, tree.Children);
        Assert.IsTrue(tree.Children[0].IsResolved);
        Assert.IsFalse(tree.Children[0].IsSystem);
    }

    [TestMethod]
    public void BuildTree_Nested_ReturnsDeepTree()
    {
        CreateFile("types.h", "// leaf");
        CreateFile("mylib.h", "#include \"types.h\"");
        var main = CreateFile("main.cpp", "#include \"mylib.h\"");

        var tree = IncludeScanner.BuildTree(main, [], includeSystem: true);

        Assert.HasCount(1, tree.Children);
        var mylib = tree.Children[0];
        Assert.HasCount(1, mylib.Children);
        Assert.IsTrue(mylib.Children[0].IsResolved);
    }

    [TestMethod]
    public void CollectResolvedFiles_ReturnsTransitiveHeadersWithoutRoot()
    {
        var types = CreateFile("include/types.h", "// leaf");
        var library = CreateFile("include/library.h", "#include \"types.h\"");
        var main = CreateFile("src/main.cpp", "#include \"library.h\"");

        var files = IncludeScanner.CollectResolvedFiles(
            main, [Path.Combine(_tempDir, "include")]);

        CollectionAssert.AreEquivalent(new[] { library, types }, files.ToArray());
    }

    [TestMethod]
    public void BuildTree_CircularIncludes_NoCycle()
    {
        // a.h includes b.h, b.h includes a.h — should not loop
        CreateFile("a.h", "#include \"b.h\"");
        CreateFile("b.h", "#include \"a.h\"");
        var main = CreateFile("main.cpp", "#include \"a.h\"");

        // Should complete without stack overflow
        var tree = IncludeScanner.BuildTree(main, [], includeSystem: true);

        Assert.IsTrue(tree.IsResolved);
        Assert.HasCount(1, tree.Children); // a.h
        var aNode = tree.Children[0];
        Assert.HasCount(1, aNode.Children); // b.h
        var bNode = aNode.Children[0];
        // b.h includes a.h, but a.h was already visited — returned as a leaf with no children
        Assert.HasCount(1, bNode.Children);   // a.h leaf
        Assert.HasCount(0, bNode.Children[0].Children); // no further expansion
    }

    [TestMethod]
    public void BuildTree_MaxDepth_LimitsRecursion()
    {
        CreateFile("c.h", "// leaf");
        CreateFile("b.h", "#include \"c.h\"");
        CreateFile("a.h", "#include \"b.h\"");
        var main = CreateFile("main.cpp", "#include \"a.h\"");

        var tree = IncludeScanner.BuildTree(main, [], includeSystem: true, maxDepth: 1);

        // depth=1 means root's children are leaves (no grandchildren)
        Assert.HasCount(1, tree.Children);    // a.h
        Assert.HasCount(0, tree.Children[0].Children); // b.h not expanded
    }

    [TestMethod]
    public void BuildTree_NoSystemIncludes_SystemNodesAreLeaves()
    {
        var main = CreateFile("main.cpp", """
            #include <stdio.h>
            #include "mylib.h"
            """);
        CreateFile("mylib.h", "#include <string.h>");

        var tree = IncludeScanner.BuildTree(main, [], includeSystem: false);

        // stdio.h should be present but with no children
        var stdioNode = tree.Children.FirstOrDefault(n => n.IsSystem);
        Assert.IsNotNull(stdioNode);
        Assert.HasCount(0, stdioNode.Children);

        // mylib.h is resolved; its child <string.h> should also be leaf
        var mylibNode = tree.Children.FirstOrDefault(n => !n.IsSystem);
        Assert.IsNotNull(mylibNode);
        Assert.IsTrue(mylibNode.IsResolved);
        if (mylibNode.Children.Count > 0)
            Assert.IsTrue(mylibNode.Children[0].IsSystem);
    }

    [TestMethod]
    public void BuildTree_UnresolvedHeader_MarkedNotFound()
    {
        var main = CreateFile("main.cpp", "#include \"missing.h\"");

        var tree = IncludeScanner.BuildTree(main, [], includeSystem: true);

        Assert.HasCount(1, tree.Children);
        Assert.IsFalse(tree.Children[0].IsResolved);
        Assert.AreEqual("missing.h", tree.Children[0].FilePath);
    }
}
