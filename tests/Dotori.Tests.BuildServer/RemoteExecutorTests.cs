using Dotori.Core.Build;
using Dotori.Grpc;
using Grpc.Core;

namespace Dotori.Tests.BuildServer;

[TestClass]
public sealed class RemoteExecutorTests
{
    [TestMethod]
    public void RemoteExecutor_RetainsRequestedTargetTriple()
    {
        using var executor = new RemoteExecutor(
            "http://127.0.0.1:1", "aarch64-unknown-linux-musl");

        Assert.AreEqual("aarch64-unknown-linux-musl", executor.TargetTriple);
    }

    [TestMethod]
    public void RemoteExecutor_ClassifiesWrappedHttpFailuresAsTransportErrors()
    {
        var wrappedHttpFailure = new RpcException(new Status(
            StatusCode.Internal,
            "Error starting gRPC call. HttpRequestException: HTTP/2 connection closed."));
        var applicationFailure = new RpcException(new Status(
            StatusCode.Internal,
            "Worker rejected the compiler request."));

        Assert.IsTrue(RemoteExecutor.IsTransportError(wrappedHttpFailure));
        Assert.IsFalse(RemoteExecutor.IsTransportError(applicationFailure));
    }

    [TestMethod]
    public void RemoteExecutor_SendsOnlyExecutableNameToWorker()
    {
        var compiler = OperatingSystem.IsWindows()
            ? @"C:\LLVM\bin\clang++.exe"
            : "/opt/toolchains/llvm/bin/clang++";

        Assert.AreEqual(
            OperatingSystem.IsWindows() ? "clang++.exe" : "clang++",
            RemoteExecutor.NormalizeRemoteExecutable(compiler));
        Assert.AreEqual("g++", RemoteExecutor.NormalizeRemoteExecutable("g++"));
    }

    [TestMethod]
    public void RemoteArgumentMapper_ReplacesCompilePathsWithoutDuplicates()
    {
        var job = new CompileJob
        {
            SourceFile = "/local/src/main.cpp",
            OutputFile = "/local/obj/main.o",
            Args = ["-c", "\"/local/src/main.cpp\"", "-o \"/local/obj/main.o\""],
        };

        var mapped = RemoteArgumentMapper.MapCompile(job);
        var materialized = RemoteArgumentMapper.MaterializeCompile(
            mapped, "/worker/main.cpp", "/worker/main.o");

        Assert.IsFalse(materialized.Any(arg => arg.Contains("/local/")));
        Assert.AreEqual(1, materialized.Count(arg => arg.Contains("/worker/main.cpp")));
        Assert.AreEqual(1, materialized.Count(arg => arg.Contains("/worker/main.o")));
    }

    [TestMethod]
    public void RemoteArgumentMapper_PreservesArchiverArgumentShape()
    {
        var job = new LinkJob
        {
            InputFiles = ["/local/a.o", "/local/b.o"],
            OutputFile = "/local/libsample.a",
            Args = ["rcs", "\"/local/libsample.a\"", "\"/local/a.o\"", "\"/local/b.o\""],
        };

        var mapped = RemoteArgumentMapper.MapLink(job);
        var materialized = RemoteArgumentMapper.MaterializeLink(
            mapped, ["/worker/input_0.o", "/worker/input_1.o"], "/worker/libsample.a");

        CollectionAssert.AreEqual(new[]
        {
            "rcs", "\"/worker/libsample.a\"", "\"/worker/input_0.o\"", "\"/worker/input_1.o\"",
        }, materialized.ToArray());
    }
}
