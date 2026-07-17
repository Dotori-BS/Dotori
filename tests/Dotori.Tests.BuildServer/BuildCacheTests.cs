using Dotori.BuildServer.Cache;
using Dotori.Grpc;
using System.Net;

namespace Dotori.Tests.BuildServer;

[TestClass]
public sealed class BuildCacheTests
{
    [TestMethod]
    public void Put_WritesAndReadsObjectBytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new BuildCache(dir);
            cache.Put("key", [1, 2, 3]);

            Assert.IsTrue(cache.TryGet("key", out var bytes));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bytes);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void RemoteBuildAuth_AllowsOnlyLoopbackWithoutConfiguredToken()
    {
        Assert.IsTrue(RemoteBuildAuth.IsAuthorized(null, null, IPAddress.Loopback));
        Assert.IsFalse(RemoteBuildAuth.IsAuthorized(null, null, IPAddress.Parse("10.0.0.8")));
    }

    [TestMethod]
    public void RemoteBuildAuth_RequiresExactConfiguredToken()
    {
        Assert.IsTrue(RemoteBuildAuth.IsAuthorized("secret", "secret", IPAddress.Parse("10.0.0.8")));
        Assert.IsFalse(RemoteBuildAuth.IsAuthorized("secret", "wrong", IPAddress.Loopback));
        Assert.IsFalse(RemoteBuildAuth.IsAuthorized("secret", null, IPAddress.Loopback));
    }

    [TestMethod]
    public void RemoteBuildAuth_HttpsRequirementIsOptionalAndOffByDefault()
    {
        Assert.IsTrue(RemoteBuildAuth.IsAuthorized(
            "secret", "secret", IPAddress.Loopback, isHttps: false));
        Assert.IsFalse(RemoteBuildAuth.IsAuthorized(
            "secret", "secret", IPAddress.Loopback, isHttps: false, requireHttps: true));
        Assert.IsTrue(RemoteBuildAuth.IsAuthorized(
            "secret", "secret", IPAddress.Loopback, isHttps: true, requireHttps: true));
    }

    [TestMethod]
    public void RemoteBuildAuth_ResolvesTokenFromSecretFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "secret-from-file\n");
            Assert.AreEqual("secret-from-file", RemoteBuildAuth.ResolveToken(null, path));
            Assert.AreEqual("direct", RemoteBuildAuth.ResolveToken("direct", path));
        }
        finally
        {
            File.Delete(path);
        }
    }

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

    [TestMethod]
    public void ComputeKey_DistinguishesCompilerAndDependencyFingerprints()
    {
        var first = BuildCache.ComputeKey(
            "source", ["-O2"], "clang", "x86_64-linux", "clang-v1", "headers-v1");
        var compilerChanged = BuildCache.ComputeKey(
            "source", ["-O2"], "clang", "x86_64-linux", "clang-v2", "headers-v1");
        var headerChanged = BuildCache.ComputeKey(
            "source", ["-O2"], "clang", "x86_64-linux", "clang-v1", "headers-v2");

        Assert.AreNotEqual(first, compilerChanged);
        Assert.AreNotEqual(first, headerChanged);
    }
}
