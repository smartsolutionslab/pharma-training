using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// Every module's service as the edge sees it: a server on a free local port that answers any request with 200, the JSON
/// <c>{"service":"stub","path":...}</c> and the header <c>X-Module-Service: stub</c>, and remembers what it received,
/// the <c>X-Forwarded-For</c> it was sent among it (<see cref="ReceivedForwardedFor"/>). The
/// edge finds it under each module's name through service discovery (<c>services:&lt;module&gt;:http:0</c>).
/// </summary>
public sealed class ModuleService : IAsyncDisposable
{
    public const string MarkerHeader = "X-Module-Service";

    private readonly WebApplication app;

    private readonly ConcurrentQueue<ReceivedRequest> received;

    private readonly ConcurrentQueue<string> forwardedFor;

    private ModuleService(WebApplication app, ConcurrentQueue<ReceivedRequest> received, ConcurrentQueue<string> forwardedFor)
    {
        this.app = app;
        this.received = received;
        this.forwardedFor = forwardedFor;
    }

    public Uri Address => new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.First()
        ?? throw new InvalidOperationException("The stub module service has no address; it has not started."));

    public IReadOnlyList<ReceivedRequest> Received => [.. received];

    /// <summary>The <c>X-Forwarded-For</c> of each request received, in order, its values joined as one list; empty without one.</summary>
    public IReadOnlyList<string> ReceivedForwardedFor => [.. forwardedFor];

    public static async Task<ModuleService> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        ConcurrentQueue<ReceivedRequest> received = [];
        ConcurrentQueue<string> forwardedFor = [];
        app.Run(async context =>
        {
            forwardedFor.Enqueue(string.Join(", ", context.Request.Headers["X-Forwarded-For"].ToArray()));
            received.Enqueue(new ReceivedRequest(
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Request.QueryString.Value ?? string.Empty,
                context.Request.Headers.Authorization.FirstOrDefault()));
            context.Response.Headers[MarkerHeader] = "stub";
            await context.Response.WriteAsJsonAsync(new { service = "stub", path = context.Request.Path.Value }, context.RequestAborted);
        });
        await app.StartAsync(cancellationToken);
        return new ModuleService(app, received, forwardedFor);
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
