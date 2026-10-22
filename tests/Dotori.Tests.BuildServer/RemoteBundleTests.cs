using System.Security.Cryptography;
using System.Text.Json;
using Dotori.Grpc;
using Dotori.BuildServer.Workers;
using Dotori.Worker.Services;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dotori.Tests.BuildServer;

[TestClass]
public sealed class RemoteBundleTests
{
    private string _directory = "";

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dotori-bundle-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public async Task ContentStore_ReceivesVerifiedChunksAndFindsMissingContent()
    {
        var store = new RemoteContentStore(_directory);
        byte[] content = [1, 2, 3, 4];
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var query = new BlobQuery(); query.Hashes.Add(hash);
        Assert.HasCount(1, store.Missing(query).Hashes);
        var receipt = await store.ReceiveAsync(new Reader([
            new BlobChunk { Hash = hash, Data = ByteString.CopyFrom([1, 2]) },
            new BlobChunk { Hash = hash, Data = ByteString.CopyFrom([3, 4]) },
        ]), CancellationToken.None);
        Assert.AreEqual(4L, receipt.Size);
        Assert.IsEmpty(store.Missing(query).Hashes);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(store.BlobPath(hash)));
    }

    [TestMethod]
    public async Task ContentStore_RejectsCorruptionEvenIfHashIsAlreadyCached()
    {
        var source = Path.Combine(_directory, "source");
        File.WriteAllText(source, "correct");
        var store = new RemoteContentStore(Path.Combine(_directory, "cache"));
        var hash = await store.ImportFileAsync(source, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReceiveAsync(
            new Reader([new BlobChunk { Hash = hash, Data = ByteString.CopyFromUtf8("wrong") }]), CancellationToken.None));
        Assert.AreEqual("correct", File.ReadAllText(store.BlobPath(hash)));
        Assert.IsEmpty(Directory.GetFiles(store.DirectoryPath, "*.upload"));
    }

    [TestMethod]
    public async Task ContentStore_RejectsOversizedUpload()
    {
        var store = new RemoteContentStore(_directory, maxBlobBytes: 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReceiveAsync(new Reader([
            new BlobChunk { Hash = new string('a', 64), Data = ByteString.CopyFrom([1, 2]) },
        ]), CancellationToken.None));
        Assert.IsEmpty(Directory.GetFiles(_directory, "*.upload"));
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("root/../../escape")]
    [DataRow("/absolute")]
    [DataRow("C:/absolute")]
    [DataRow("root\\escape")]
    [DataRow("root//empty")]
    public void BundlePaths_RejectEscapes(string path) =>
        Assert.ThrowsExactly<InvalidDataException>(() => RemoteContentStore.SafePath(_directory, path));

    [TestMethod]
    public async Task BundleClient_RebasesToolsHeadersEnvironmentAndOutputWithoutCopyingToolsAsInputs()
    {
        var tools = Path.Combine(_directory, "tools"); Directory.CreateDirectory(tools);
        var workspace = Path.Combine(_directory, "project"); Directory.CreateDirectory(workspace);
        var executable = Path.Combine(tools, "clang.exe"); File.WriteAllText(executable, "compiler");
        var source = Path.Combine(workspace, "main.cpp"); File.WriteAllText(source, "#include \"header.h\"");
        File.WriteAllText(Path.Combine(workspace, "header.h"), "#define VALUE 1");
        File.WriteAllText(Path.Combine(workspace, ".env"), "PRIVATE_TOKEN=secret");
        Directory.CreateDirectory(Path.Combine(workspace, ".aws"));
        File.WriteAllText(Path.Combine(workspace, ".aws", "credentials"), "private credentials");
        File.WriteAllText(Path.Combine(workspace, "private.pfx"), "private certificate");
        var configuration = Path.Combine(_directory, "bundle.json");
        File.WriteAllText(configuration, JsonSerializer.Serialize(new
        {
            toolchainRoots = new[] { tools }, inputRoots = new[] { workspace }, workingDirectory = workspace,
            inputExcludes = new[] { "*.pfx" },
            environment = new Dictionary<string, string> { ["SDKROOT"] = tools },
        }));
        var client = new RemoteBundleClient(configuration);
        var output = Path.Combine(workspace, ".dotori-cache", "main.obj");
        var prepared = await client.PrepareAsync(executable,
            ["-I\"" + workspace + "\"", "-c", "\"" + source + "\"", "-o", "\"" + output + "\""],
            output, [source], CancellationToken.None);
        Assert.AreEqual("r0/clang.exe", prepared.Bundle.Executable);
        Assert.AreEqual("r0", prepared.Bundle.WorkingDirectory);
        Assert.AreEqual(RemoteBundleClient.ToolchainPlaceholder + "/r0", prepared.Bundle.Toolchain.Environment["SDKROOT"]);
        Assert.HasCount(1, prepared.Bundle.Toolchain.Files);
        Assert.HasCount(2, prepared.Bundle.Inputs);
        Assert.IsTrue(prepared.Arguments.All(argument => !argument.Contains(workspace)));
        RemoteBundleRunner.Validate(prepared.Bundle);
        var before = prepared.Bundle.Id;
        File.WriteAllText(Path.Combine(workspace, "header.h"), "#define VALUE 2");
        var changed = await client.PrepareAsync(executable, [], output, [source], CancellationToken.None);
        Assert.AreNotEqual(before, changed.Bundle.Id);
        changed.Bundle.Toolchain.Id = new string('0', 64);
        Assert.ThrowsExactly<InvalidDataException>(() => RemoteBundleRunner.Validate(changed.Bundle));
    }

    [TestMethod]
    public async Task Worker_UploadedToolsAreDisabledByDefault()
    {
        var service = new WorkerService(NullLogger<WorkerService>.Instance);
        var error = await Assert.ThrowsAsync<RpcException>(() => service.FindMissingBlobs(new BlobQuery(), null!));
        Assert.AreEqual(StatusCode.PermissionDenied, error.StatusCode);
    }

    [TestMethod]
    public void WorkerRouting_UploadedToolchainsDoNotRequireInstalledTargetToolchains()
    {
        using var channel = GrpcChannel.ForAddress("http://127.0.0.1:1");
        var worker = new WorkerEntry("local", channel, new BuildWorker.BuildWorkerClient(channel), new Metadata(), TimeSpan.FromSeconds(1));
        worker.SetSupportedTargets([]);
        worker.SetBundleHost("linux", "x64", true);
        Assert.IsTrue(worker.SupportsBundleHost("linux", "x64"));
        Assert.IsFalse(worker.SupportsBundleHost("windows", "x64"));
        Assert.IsFalse(worker.SupportsBundleHost("linux", "arm64"));
        Assert.IsFalse(worker.SupportsTarget("x86_64-unknown-linux-gnu"));
    }

    private sealed class Reader(IReadOnlyList<BlobChunk> chunks) : IAsyncStreamReader<BlobChunk>
    {
        private int _position = -1;
        public BlobChunk Current => chunks[_position];
        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(++_position < chunks.Count);
    }
}
