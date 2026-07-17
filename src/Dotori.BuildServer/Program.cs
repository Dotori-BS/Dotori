using Dotori.BuildServer.Cache;
using Dotori.BuildServer.Services;
using Dotori.BuildServer.Workers;
using Dotori.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<BuildCache>();
builder.Services.AddSingleton<WorkerPool>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Dotori.Grpc.BuildCoordinator") &&
        !RemoteBuildAuth.IsAuthorized(
            RemoteBuildAuth.ResolveToken(
                builder.Configuration["DOTORI_SERVER_TOKEN"],
                builder.Configuration["DOTORI_SERVER_TOKEN_FILE"]),
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

app.MapGrpcService<CoordinatorService>();
app.MapGet("/", () => "dotori build server — use a gRPC client");

app.Run();
