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
    public void ShouldTrigger_IgnoresMetadataAndBuildOutputs(string relativePath)
    {
        Assert.IsFalse(BuildWatchFilter.ShouldTrigger(
            Path.Combine(Path.GetTempPath(), "project", relativePath)));
    }
}
