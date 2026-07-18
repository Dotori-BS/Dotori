using Dotori.Core.Location;

namespace Dotori.Tests.Graph;

[TestClass]
public sealed class ProjectLocatorTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    // ─── ResolveExplicitPath ─────────────────────────────────────────────────

    [TestMethod]
    public void ResolveExplicitPath_DirectDotoriFile_ReturnsPath()
    {
        var path = FixturePath("single-project/.dotori");
        var result = ProjectLocator.ResolveExplicitPath(path);
        Assert.AreEqual(Path.GetFullPath(path), result);
    }

    [TestMethod]
    public void ResolveExplicitPath_Directory_ReturnsDotoriInside()
    {
        var dir = FixturePath("single-project");
        var result = ProjectLocator.ResolveExplicitPath(dir);
        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(dir, ".dotori")),
            result);
    }

    [TestMethod]
    public void ResolveExplicitPath_DirectoryWithoutDotori_Throws()
    {
        var dir = FixturePath("multi-project");  // no .dotori at root
        Assert.ThrowsExactly<ProjectLocatorException>(() =>
            ProjectLocator.ResolveExplicitPath(dir));
    }

    [TestMethod]
    public void ResolveExplicitPath_NonexistentPath_Throws()
    {
        Assert.ThrowsExactly<ProjectLocatorException>(() =>
            ProjectLocator.ResolveExplicitPath("/nonexistent/path/.dotori"));
    }

    // ─── FindDotoriFiles ─────────────────────────────────────────────────────

    [TestMethod]
    public void FindDotoriFiles_CurrentDirHasDotori_ReturnsSingle()
    {
        var dir = FixturePath("single-project");
        var found = ProjectLocator.FindDotoriFiles(dir);
        Assert.HasCount(1, found);
        StringAssert.EndsWith(found[0], ".dotori");
    }

    [TestMethod]
    public void FindDotoriFiles_Subdirectories_FindsMultiple()
    {
        var dir = FixturePath("multi-project");
        var found = ProjectLocator.FindDotoriFiles(dir);
        Assert.HasCount(2, found);
        Assert.IsTrue(found.Any(p => p.Contains("app")));
        Assert.IsTrue(found.Any(p => p.Contains("lib")));
    }

    [TestMethod]
    public void FindDotoriFiles_EmptyDir_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var found = ProjectLocator.FindDotoriFiles(tempDir);
            Assert.IsEmpty(found);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void FindDotoriFiles_AncestorSearch_FindsParent()
    {
        // Create temp tree: root/.dotori  +  root/subdir/ (no .dotori)
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var subDir   = Path.Combine(tempRoot, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(tempRoot, ".dotori"), "project P { type = executable }");
        try
        {
            var found = ProjectLocator.FindDotoriFiles(subDir);
            Assert.HasCount(1, found);
            Assert.StartsWith(tempRoot, found[0]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void FindAllDotoriFiles_IncludesRootAndNestedProjects()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var nested = Path.Combine(tempRoot, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(tempRoot, ".dotori"), "project Root { type = test }");
        File.WriteAllText(Path.Combine(nested, ".dotori"), "project Nested { type = test }");
        try
        {
            var found = ProjectLocator.FindAllDotoriFiles(tempRoot);

            Assert.HasCount(2, found);
            CollectionAssert.AreEqual(new[]
            {
                Path.Combine(tempRoot, ".dotori"),
                Path.Combine(nested, ".dotori"),
            }, found.ToArray());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ─── LoadProject ─────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadProject_SingleApp_ParsesNameAndType()
    {
        var path = FixturePath("single-project/.dotori");
        var proj = ProjectLocator.LoadProject(path);
        Assert.AreEqual("SingleApp", proj.ProjectName);
        Assert.AreEqual("executable", proj.ProjectType);
    }

    [TestMethod]
    public void LoadProject_LibProject_ParsesStaticLibType()
    {
        var path = FixturePath("multi-project/lib/.dotori");
        var proj = ProjectLocator.LoadProject(path);
        Assert.AreEqual("LibProject", proj.ProjectName);
        Assert.AreEqual("staticlibrary", proj.ProjectType);
    }

    // ─── PromptSelection ─────────────────────────────────────────────────────

    [TestMethod]
    public void PromptSelection_BuildAll_ReturnsAll()
    {
        var projects = new[]
        {
            MakeProject("A", FixturePath("multi-project/app/.dotori")),
            MakeProject("B", FixturePath("multi-project/lib/.dotori")),
        };

        var result = ProjectLocator.PromptSelection(projects, buildAll: true);
        Assert.HasCount(2, result);
    }

    [TestMethod]
    public void PromptSelection_SingleProject_ReturnsWithoutPrompt()
    {
        var projects = new[] { MakeProject("A", FixturePath("single-project/.dotori")) };
        var output   = new StringWriter();

        var result = ProjectLocator.PromptSelection(projects, stdout: output);

        // Should not have printed any prompt
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.HasCount(1, result);
    }

    [TestMethod]
    public void PromptSelection_NumberChoice_ReturnsSingle()
    {
        var projects = new[]
        {
            MakeProject("App", FixturePath("multi-project/app/.dotori")),
            MakeProject("Lib", FixturePath("multi-project/lib/.dotori")),
        };

        var input  = new StringReader("1\n");
        var output = new StringWriter();

        var result = ProjectLocator.PromptSelection(projects, stdin: input, stdout: output);

        Assert.HasCount(1, result);
        StringAssert.Contains(result[0], "app");
    }

    [TestMethod]
    public void PromptSelection_EmptyInput_ReturnsAll()
    {
        var projects = new[]
        {
            MakeProject("App", FixturePath("multi-project/app/.dotori")),
            MakeProject("Lib", FixturePath("multi-project/lib/.dotori")),
        };

        var input  = new StringReader("\n");
        var output = new StringWriter();

        var result = ProjectLocator.PromptSelection(projects, stdin: input, stdout: output);

        Assert.HasCount(2, result);
    }

    [TestMethod]
    public void PromptSelection_InvalidInput_Throws()
    {
        var projects = new[]
        {
            MakeProject("App", FixturePath("multi-project/app/.dotori")),
            MakeProject("Lib", FixturePath("multi-project/lib/.dotori")),
        };

        var input  = new StringReader("99\n");
        var output = new StringWriter();

        Assert.ThrowsExactly<ProjectLocatorException>(() =>
            ProjectLocator.PromptSelection(projects, stdin: input, stdout: output));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static LocatedProject MakeProject(string name, string path) =>
        new()
        {
            ProjectName = name,
            ProjectType = "executable",
            DotoriPath  = path,
            ProjectDir  = Path.GetDirectoryName(path)!,
        };
}
