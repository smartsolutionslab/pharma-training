using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// One test's edge: the module service stub, the web root and the edge over them, disposed together. Published, the edge
/// runs in Production with the web app's build in its web root; run mode starts it in Development without one.
/// </summary>
public sealed class EdgeHost : IAsyncDisposable
{
    /// <summary>The realm the edge validates tokens from and tells the browser about.</summary>
    public const string Authority = "https://auth.edge-tests.example/realms/pharma-training";

    private readonly WebRoot webRoot;

    private readonly EdgeWithWebRoot edge;

    private EdgeHost(ModuleService service, WebRoot webRoot, EdgeWithWebRoot edge)
    {
        Service = service;
        this.webRoot = webRoot;
        this.edge = edge;
    }

    public ModuleService Service { get; }

    /// <summary>The edge with the web app's build in its web root, as published.</summary>
    public static Task<EdgeHost> ServingTheAppAsync(string environment) => StartAsync(environment, WebRoot.WithTheApp(), null);

    /// <summary>The edge with the web app's build in its web root, as published, with these further settings.</summary>
    public static Task<EdgeHost> ServingTheAppAsync(string environment, IReadOnlyDictionary<string, string> settings) => StartAsync(environment, WebRoot.WithTheApp(), settings);

    /// <summary>The edge with an empty web root, as run mode starts it.</summary>
    public static Task<EdgeHost> WithoutTheAppAsync(string environment) => StartAsync(environment, WebRoot.Empty(), null);

    /// <summary>A client of the edge that follows no redirect, so every answer is the edge's own.</summary>
    public HttpClient CreateClient() => edge.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async ValueTask DisposeAsync()
    {
        await edge.DisposeAsync();
        await Service.DisposeAsync();
        webRoot.Dispose();
    }

    private static async Task<EdgeHost> StartAsync(string environment, WebRoot webRoot, IReadOnlyDictionary<string, string>? settings)
    {
        var service = await ModuleService.StartAsync(TestContext.Current.CancellationToken);
        var edge = new EdgeWithWebRoot(environment, webRoot.Path, service.Address, settings);
        var host = new EdgeHost(service, webRoot, edge);

        // The answers below are the edge's over this web root, or the test would prove nothing about it.
        edge.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath
            .ShouldBe(webRoot.Path, "The edge did not take the test's web root; every answer would be about another directory.");
        return host;
    }
}
