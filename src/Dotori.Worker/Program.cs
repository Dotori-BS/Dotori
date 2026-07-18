using Dotori.Worker.Services;
using Dotori.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Worker deployments commonly use h2c inside a trusted network or container mesh.
builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2));

builder.Services.AddGrpc();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Dotori.Grpc.BuildWorker") &&
        !RemoteBuildAuth.IsAuthorized(
            RemoteBuildAuth.ResolveToken(
                builder.Configuration["DOTORI_WORKER_TOKEN"],
                builder.Configuration["DOTORI_WORKER_TOKEN_FILE"]),
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
