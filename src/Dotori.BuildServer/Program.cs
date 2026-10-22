using Dotori.BuildServer.Cache;
using Dotori.BuildServer.Services;
using Dotori.BuildServer.Workers;
using Dotori.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Plain-text gRPC requires HTTP/2 prior knowledge. Without this explicit default,
// Kestrel selects HTTP/1.1 for http:// endpoints and rejects every gRPC request.
builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2));

builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = 64 * 1024 * 1024);
builder.Services.AddSingleton(_ => new RemoteContentStore(
    builder.Configuration["BUILD_CONTENT_CACHE_DIR"] ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotori", "coordinator-content"),
    builder.Configuration.GetValue<long?>("DOTORI_MAX_BLOB_BYTES") ?? 8L * 1024 * 1024 * 1024));
builder.Services.AddSingleton(_ =>
{
    var cacheDir = builder.Configuration["BUILD_CACHE_DIR"];
    var maxBytes = builder.Configuration.GetValue<long>("BUILD_CACHE_MAX_BYTES", 0);
    var maxAgeDays = builder.Configuration.GetValue<double>("BUILD_CACHE_MAX_AGE_DAYS", 0);
    return new BuildCache(
        cacheDir,
        maxBytes,
        maxAgeDays > 0 ? TimeSpan.FromDays(maxAgeDays) : null);
});
builder.Services.AddSingleton<WorkerPool>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/BuildCoordinator") &&
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
