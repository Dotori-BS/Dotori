using System.Text.Json;
using Dotori.Core.Build;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class BuildTraceTests
{
    [TestMethod]
    public void Finish_AggregatesTimingsAndIncrementalReuse()
    {
        var trace = new BuildTraceSession("linux-x64", "debug");
        var reused = trace.BeginProject("cached", "/project/cached.dotori");
        reused.RecordCompiles(inputs: 4, compiled: 1);
        reused.RecordLink(executed: false);
        reused.Complete(success: true);
        var rebuilt = trace.BeginProject("rebuilt", "/project/rebuilt.dotori");
        rebuilt.RecordCompiles(inputs: 2, compiled: 2);
        rebuilt.RecordLink(executed: true);
        rebuilt.Complete(success: false);

        var document = trace.Finish();

        Assert.AreEqual(2, document.Summary.ProjectCount);
        Assert.AreEqual(1, document.Summary.SucceededProjects);
        Assert.AreEqual(6, document.Summary.CompileInputs);
        Assert.AreEqual(3, document.Summary.CompiledInputs);
        Assert.AreEqual(3, document.Summary.ReusedCompileInputs);
        Assert.AreEqual(1, document.Summary.ExecutedLinks);
        Assert.AreEqual(1, document.Summary.ReusedLinks);
        Assert.AreEqual(50, document.Summary.ReusePercent);
    }

    [TestMethod]
    public void WriteJson_ProducesVersionedMachineReadableReport()
    {
        var trace = new BuildTraceSession("host", "release");
        var project = trace.BeginProject("app", "/project/.dotori");
        project.RecordCompiles(1, 0);
        project.RecordLink(executed: false);
        project.Complete(success: true);
        var path = Path.Combine(Path.GetTempPath(), $"dotori-trace-{Guid.NewGuid():N}.json");
        try
        {
            BuildTraceSession.WriteJson(trace.Finish(), path);

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.AreEqual("host", json.RootElement.GetProperty("Target").GetString());
            Assert.AreEqual(100, json.RootElement.GetProperty("Summary")
                .GetProperty("ReusePercent").GetDouble());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
