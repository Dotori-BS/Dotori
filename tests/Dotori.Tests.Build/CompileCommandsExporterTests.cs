using System.Text.Json;
using Dotori.Core.Build;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class CompileCommandsExporterTests
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

    [TestMethod]
    public async Task WriteAsync_CreatesParentDirectoryAndValidJson()
    {
        var output = Path.Combine(_tempDir, "nested", "compile_commands.json");
        var entry = MakeEntry("main.cpp", "clang++ -c main.cpp");

        await CompileCommandsExporter.WriteAsync(output, [entry]);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.AreEqual(1, document.RootElement.GetArrayLength());
        Assert.AreEqual("main.cpp", document.RootElement[0].GetProperty("file").GetString());
    }

    [TestMethod]
    public async Task WriteAsync_SortsAndRemovesExactDuplicates()
    {
        var output = Path.Combine(_tempDir, "compile_commands.json");
        var second = MakeEntry("z.cpp", "clang++ -c z.cpp");
        var first = MakeEntry("a.cpp", "clang++ -c a.cpp");

        await CompileCommandsExporter.WriteAsync(output, [second, first, first]);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.AreEqual(2, document.RootElement.GetArrayLength());
        Assert.AreEqual("a.cpp", document.RootElement[0].GetProperty("file").GetString());
        Assert.AreEqual("z.cpp", document.RootElement[1].GetProperty("file").GetString());
        Assert.IsEmpty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [TestMethod]
    public void ClangdConfig_WindowsPath_UsesYamlSafeQuoting()
    {
        var content = ClangdConfigGenerator.BuildContent(
            @"C:\Program Files\LLVM\bin\clang++.exe",
            @"C:\Program Files\LLVM\lib\clang\21");

        StringAssert.Contains(content, @"Compiler: 'C:\Program Files\LLVM\bin\clang++.exe'");
        StringAssert.Contains(content, @"'-resource-dir=C:\Program Files\LLVM\lib\clang\21'");
    }

    [TestMethod]
    public void ClangdConfig_SingleQuote_IsEscaped()
    {
        var content = ClangdConfigGenerator.BuildContent("/tmp/user's/clang++");

        StringAssert.Contains(content, "Compiler: '/tmp/user''s/clang++'");
    }

    private CompileCommandEntry MakeEntry(string file, string command) => new()
    {
        Directory = _tempDir,
        File = file,
        Command = command,
        Output = Path.ChangeExtension(file, ".o"),
    };
}
