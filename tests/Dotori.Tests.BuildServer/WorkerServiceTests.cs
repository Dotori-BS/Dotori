using Dotori.Grpc;
using Dotori.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Google.Protobuf;

namespace Dotori.Tests.BuildServer;

[TestClass]
public sealed class WorkerServiceTests
{
    [TestMethod]
    [DataRow("../outside")]
    [DataRow("nested/output")]
    [DataRow("")]
    public async Task Link_RejectsUnsafeOutputName(string outputName)
    {
        var service = new WorkerService(NullLogger<WorkerService>.Instance);

        var response = await service.Link(
            new WorkerLinkRequest { OutputName = outputName }, null!);

        Assert.IsFalse(response.Success);
        Assert.AreEqual(-1, response.ExitCode);
    }

    [TestMethod]
    public async Task Link_RejectsExecutableOutsideDefaultAllowlist()
    {
        var service = new WorkerService(NullLogger<WorkerService>.Instance);

        var response = await service.Link(
            new WorkerLinkRequest { OutputName = "output.o", Linker = "/bin/sh" }, null!);

        Assert.IsFalse(response.Success);
        Assert.AreEqual(-1, response.ExitCode);
    }

    [TestMethod]
    public void ExecutableAllowlist_CanonicalizesWindowsSuffixAndPaths()
    {
        Assert.AreEqual("cl", WorkerService.CanonicalExecutableName("cl.exe"));
        Assert.AreEqual("clang++", WorkerService.CanonicalExecutableName("/usr/bin/clang++"));
    }

    [TestMethod]
    public async Task Link_RejectsInputOverConfiguredLimit()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTORI_MAX_INPUT_BYTES"] = "2",
        }).Build();
        var service = new WorkerService(NullLogger<WorkerService>.Instance, config);
        var request = new WorkerLinkRequest { OutputName = "output.o", Linker = "clang++" };
        request.ObjFiles.Add(ByteString.CopyFrom([1, 2, 3]));

        var response = await service.Link(request, null!);

        Assert.IsFalse(response.Success);
        StringAssert.Contains(response.Stderr, "limits");
    }

    [TestMethod]
    public async Task Health_ReportsConfiguredTargetTriples()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTORI_SUPPORTED_TARGETS"] =
                "x86_64-unknown-linux-gnu, aarch64-unknown-linux-musl",
        }).Build();
        var service = new WorkerService(NullLogger<WorkerService>.Instance, config);

        var response = await service.Health(new HealthRequest(), null!);

        Assert.IsTrue(response.Ok);
        CollectionAssert.AreEquivalent(new[]
        {
            "x86_64-unknown-linux-gnu", "aarch64-unknown-linux-musl",
        }, response.SupportedTargets.ToArray());
    }
}
