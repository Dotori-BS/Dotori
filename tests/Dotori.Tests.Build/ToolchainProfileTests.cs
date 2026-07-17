using Dotori.Core.Toolchain;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class ToolchainProfileTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [TestMethod]
    public void Load_ResolvesRelativePathsAgainstProfile()
    {
        var path = Path.Combine(_tempDir, "linux.toolchain.json");
        File.WriteAllText(path, """
            {
              "kind": "clang",
              "compiler": "bin/clang++",
              "linker": "bin/clang++",
              "targetTriple": "aarch64-unknown-linux-musl",
              "sysroot": "sysroot",
              "assembler": { "nasm": "tools/nasm" }
            }
            """);

        var profile = ToolchainProfile.Load("linux.toolchain.json", _tempDir);

        Assert.AreEqual(CompilerKind.Clang, profile.Kind);
        Assert.AreEqual(Path.Combine(_tempDir, "bin", "clang++"), profile.CompilerPath);
        Assert.AreEqual(Path.Combine(_tempDir, "sysroot"), profile.SysRoot);
        Assert.AreEqual("aarch64-unknown-linux-musl", profile.TargetTriple);
        Assert.AreEqual(Path.Combine(_tempDir, "tools", "nasm"), profile.Assembler!.NasmPath);
    }

    [TestMethod]
    public void Load_PreservesExecutableNamesForPathLookup()
    {
        var path = Path.Combine(_tempDir, "host.json");
        File.WriteAllText(path, """
            { "kind": "clang", "compiler": "clang++", "linker": "lld", "targetTriple": "x86_64-unknown-linux-gnu" }
            """);

        var profile = ToolchainProfile.Load(path);

        Assert.AreEqual("clang++", profile.CompilerPath);
        Assert.AreEqual("lld", profile.LinkerPath);
    }

    [TestMethod]
    public void Load_RejectsUnsupportedMsvcProfile()
    {
        var path = Path.Combine(_tempDir, "msvc.json");
        File.WriteAllText(path, """
            { "kind": "msvc", "compiler": "cl", "linker": "link", "targetTriple": "x86_64-pc-windows-msvc" }
            """);

        Assert.Throws<InvalidDataException>(() => ToolchainProfile.Load(path));
    }
}
