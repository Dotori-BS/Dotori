using Dotori.Worker.Services;
using Dotori.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Dotori.Grpc.BuildWorker") &&
        !RemoteBuildAuth.IsAuthorized(
            builder.Configuration["DOTORI_WORKER_TOKEN"],
            context.Request.Headers[RemoteBuildAuth.HeaderName],
            context.Connection.RemoteIpAddress,
            context.Request.IsHttps,
            builder.Configuration.GetValue("DOTORI_REQUIRE_HTTPS", false)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next();
});

app.MapGrpcService<WorkerService>();
app.MapGet("/", () => "dotori worker — use a gRPC client");

app.Run();
