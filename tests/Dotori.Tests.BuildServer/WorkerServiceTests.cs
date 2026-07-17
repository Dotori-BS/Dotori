using Dotori.Grpc;
using Dotori.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;

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
}
