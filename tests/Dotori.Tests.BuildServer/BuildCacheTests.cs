using Dotori.BuildServer.Cache;

namespace Dotori.Tests.BuildServer;

[TestClass]
public sealed class BuildCacheTests
{
    [TestMethod]
    public void ComputeKey_DistinguishesArgumentBoundaries()
    {
        var first = BuildCache.ComputeKey("source", ["-D", "AB"], "clang", "x86_64-linux");
        var second = BuildCache.ComputeKey("source", ["-DA", "B"], "clang", "x86_64-linux");

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void ComputeKey_DistinguishesCompilerAndTarget()
    {
        var clang = BuildCache.ComputeKey("source", ["-O2"], "clang", "x86_64-linux");
        var gcc = BuildCache.ComputeKey("source", ["-O2"], "g++", "x86_64-linux");
        var arm = BuildCache.ComputeKey("source", ["-O2"], "clang", "aarch64-linux");

        Assert.AreNotEqual(clang, gcc);
        Assert.AreNotEqual(clang, arm);
    }

    [TestMethod]
    public void ComputeKey_IsStableForIdenticalInputs()
    {
        var first = BuildCache.ComputeKey("source", ["-O2", "-Wall"], "clang", "x86_64-linux");
        var second = BuildCache.ComputeKey("source", ["-O2", "-Wall"], "clang", "x86_64-linux");

        Assert.AreEqual(first, second);
    }
}
