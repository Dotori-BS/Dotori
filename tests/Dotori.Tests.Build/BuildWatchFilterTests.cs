using Dotori.Core.Build;

namespace Dotori.Tests.Build;

[TestClass]
public sealed class BuildWatchFilterTests
{
    [TestMethod]
    [DataRow("src/main.cpp")]
    [DataRow("include/api.hpp")]
    [DataRow("schemas/messages.proto")]
    [DataRow(".dotori")]
    [DataRow("dotori-lock.json")]
    public void ShouldTrigger_AcceptsProjectInputs(string relativePath)
    {
        Assert.IsTrue(BuildWatchFilter.ShouldTrigger(
            Path.Combine(Path.GetTempPath(), "project", relativePath)));
    }

    [TestMethod]
    [DataRow(".dotori-cache/obj/main.o")]
    [DataRow(".git/index")]
    [DataRow("build/app")]
    [DataRow("src/main.cpp.swp")]
    [DataRow("src/main.o")]
    [DataRow("compile_commands.json")]
    [DataRow("CMakeLists.txt")]
    [DataRow("CMakePresets.json")]
    public void ShouldTrigger_IgnoresMetadataAndBuildOutputs(string relativePath)
    {
        Assert.IsFalse(BuildWatchFilter.ShouldTrigger(
            Path.Combine(Path.GetTempPath(), "project", relativePath)));
    }

    [TestMethod]
    [DataRow(".dotori", WatcherChangeTypes.Changed, true)]
    [DataRow("nested/library.dotori", WatcherChangeTypes.Created, true)]
    [DataRow("src/main.cpp", WatcherChangeTypes.Created, true)]
    [DataRow("src/main.cpp", WatcherChangeTypes.Deleted, true)]
    [DataRow("include/api.h", WatcherChangeTypes.Renamed, true)]
    [DataRow("src/main.cpp", WatcherChangeTypes.Changed, false)]
    [DataRow("include/api.h", WatcherChangeTypes.Changed, false)]
    [DataRow(".dotori-cache/generated.cpp", WatcherChangeTypes.Created, false)]
    public void ShouldTriggerClangd_FiltersProjectEvents(
        string relativePath,
        WatcherChangeTypes changeType,
        bool expected)
    {
        Assert.AreEqual(expected, BuildWatchFilter.ShouldTriggerClangd(
            Path.Combine(Path.GetTempPath(), "project", relativePath), changeType));
    }
}
