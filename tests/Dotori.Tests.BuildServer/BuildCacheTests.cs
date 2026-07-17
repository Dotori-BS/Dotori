using Dotori.BuildServer.Cache;
using Dotori.BuildServer.Workers;
using Dotori.Grpc;
using Grpc.Net.Client;
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
    public void Prune_SizeLimit_RemovesLeastRecentlyUsedEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new BuildCache(dir);
            cache.Put("old", [1, 2, 3]);
            cache.Put("new", [4, 5, 6]);
            var oldTime = DateTime.UtcNow.AddHours(-2);
            File.SetLastWriteTimeUtc(Path.Combine(dir, "old.o"), oldTime);
            File.SetLastAccessTimeUtc(Path.Combine(dir, "old.o"), oldTime);

            _ = new BuildCache(dir, maxBytes: 3);

            Assert.IsFalse(cache.TryGet("old", out _));
            Assert.IsTrue(cache.TryGet("new", out var bytes));
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, bytes);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Prune_MaxAge_RemovesExpiredEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new BuildCache(dir);
            cache.Put("expired", [1]);
            var oldTime = DateTime.UtcNow.AddDays(-10);
            var path = Path.Combine(dir, "expired.o");
            File.SetLastWriteTimeUtc(path, oldTime);
            File.SetLastAccessTimeUtc(path, oldTime);

            _ = new BuildCache(dir, maxAge: TimeSpan.FromDays(1));

            Assert.IsFalse(cache.TryGet("expired", out _));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Prune_DefaultLimitsDisabled_PreservesEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new BuildCache(dir);
            cache.Put("entry", [1]);

            _ = new BuildCache(dir);

            Assert.IsTrue(cache.TryGet("entry", out _));
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

    [TestMethod]
    public void WorkerEntry_TransportFailureStartsCooldownAndSuccessRecovers()
    {
        using var channel = GrpcChannel.ForAddress("http://127.0.0.1:1");
        var worker = new WorkerEntry(
            "worker", channel, new BuildWorker.BuildWorkerClient(channel),
            new global::Grpc.Core.Metadata(), TimeSpan.FromMinutes(1));

        worker.ReportTransportFailure();

        Assert.IsFalse(worker.IsAvailable);
        Assert.AreEqual(1, worker.ConsecutiveFailures);
        worker.ReportSuccess();
        Assert.IsTrue(worker.IsAvailable);
        Assert.AreEqual(0, worker.ConsecutiveFailures);
    }
}
