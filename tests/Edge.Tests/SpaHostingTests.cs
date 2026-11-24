using System.Net;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// E3 and X1: published, the edge serves the web app's build from its web root. A browser's page load (a GET or HEAD with
/// <c>Sec-Fetch-Mode: navigate</c>, or whose Accept lists text/html) answers <c>index.html</c> with no-cache, under a
/// module's prefix too, since the app's pages (<c>/catalog</c>, <c>/editorial/versions/1</c>) share it with the module's
/// API, but never under <c>/api</c> and never for the edge's own paths. Every other request goes on as before: a call for
/// data reaches the module's service. Files are served to anyone; <c>/assets/*</c>, named by content, are cached for good.
/// Without <c>index.html</c> in the web root (run mode, where the dev server serves the app), none of this applies.
/// </summary>
public class SpaHostingTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task The_root_answers_the_app(string method)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Navigation("/", new HttpMethod(method)), TestContext.Current.CancellationToken);

        if (method == "GET")
        {
            await response.ShouldBeTheAppAsync("A page load of /");
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"HEAD / as a page load answered {(int)response.StatusCode}; it should answer as GET does, without a body.");
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html", "HEAD / as a page load should describe index.html.");
        }
    }

    [Theory]
    [InlineData("/catalog", true)]
    [InlineData("/editorial/versions/1", true)]
    [InlineData("/organisation/register", true)]
    [InlineData("/editorial/versions/1", false)]
    public async Task A_navigation_under_a_module_prefix_answers_the_app(string path, bool sendsFetchMetadata)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(sendsFetchMetadata ? Requests.Navigation(path) : Requests.HtmlAccepted(path), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync(sendsFetchMetadata ? $"A page load of {path} (Sec-Fetch-Mode: navigate)" : $"A page load of {path} (Accept: text/html, no Fetch Metadata)");
        edge.Service.Received.ShouldBeEmpty($"A page load of {path} reached the module's service; the app's page should be answered before the module's route.");
    }

    [Fact]
    public async Task A_request_for_data_under_a_module_prefix_is_proxied()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();
        await ShouldServeTheAppAsync(client);

        using var response = await client.SendAsync(Requests.Data("/catalog/samples"), TestContext.Current.CancellationToken);

        await response.ShouldNotBeTheAppAsync("A call for data (Accept: application/json) to /catalog/samples");
        response.Headers.Contains(ModuleService.MarkerHeader).ShouldBeTrue($"A call for data to /catalog/samples answered {(int)response.StatusCode} without reaching the catalog service.");
        edge.Service.Received.Select(request => request.Path).ShouldBe(["/catalog/samples"]);
    }

    [Fact]
    public async Task A_form_post_under_a_module_prefix_is_proxied()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();
        await ShouldServeTheAppAsync(client);

        using var response = await client.SendAsync(Requests.Navigation("/catalog/samples", HttpMethod.Post), TestContext.Current.CancellationToken);

        await response.ShouldNotBeTheAppAsync("A POST to /catalog/samples that accepts text/html");
        edge.Service.Received.Select(request => $"{request.Method} {request.Path}").ShouldBe(["POST /catalog/samples"], "Only a GET or HEAD is a page load; a POST goes to the module's service.");
    }

    [Theory]
    [InlineData("/api/nothing")]
    [InlineData("/api")]
    [InlineData("/api/")]
    public async Task An_unknown_api_path_is_not_found_even_when_navigated(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();
        await ShouldServeTheAppAsync(client);

        using var response = await client.SendAsync(Requests.Navigation(path), TestContext.Current.CancellationToken);

        await response.ShouldNotBeTheAppAsync($"A page load of {path}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"A page load of {path} should answer 404: nothing under /api is a page of the app.");
        edge.Service.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_navigation_under_api_reaches_the_service()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();
        await ShouldServeTheAppAsync(client);

        using var response = await client.SendAsync(Requests.Navigation("/api/editorial/versions/1"), TestContext.Current.CancellationToken);

        await response.ShouldNotBeTheAppAsync("A page load of /api/editorial/versions/1");
        edge.Service.Received.Select(request => request.Path).ShouldBe(["/editorial/versions/1"], "Under /api even a page load is a call of the module's API: it reaches the service without the prefix.");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/config.json")]
    [InlineData("/openapi/v1.json")]
    public async Task The_edges_own_paths_are_never_the_app(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();
        await ShouldServeTheAppAsync(client);

        using var response = await client.SendAsync(Requests.Navigation(path), TestContext.Current.CancellationToken);

        await response.ShouldNotBeTheAppAsync($"A page load of {path}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"A page load of {path} should answer the edge's own {path} (200).");
    }

    [Fact]
    public async Task A_missing_asset_is_not_found()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();
        using var present = await client.SendAsync(Requests.File(WebRoot.AssetPath), TestContext.Current.CancellationToken);
        present.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {WebRoot.AssetPath}, which the web root holds, answered {(int)present.StatusCode}: the edge serves no files from its web root.");

        using var missing = await client.SendAsync(Requests.File("/assets/missing.js"), TestContext.Current.CancellationToken);

        await missing.ShouldNotBeTheAppAsync("GET /assets/missing.js");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, "A file the web root does not hold should answer 404.");
    }

    [Fact]
    public async Task Assets_are_cached_and_the_app_is_not()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var asset = await client.SendAsync(Requests.File(WebRoot.AssetPath), TestContext.Current.CancellationToken);
        using var app = await client.SendAsync(Requests.Navigation("/catalog"), TestContext.Current.CancellationToken);

        asset.StatusCode.ShouldBe(HttpStatusCode.OK, $"An anonymous GET {WebRoot.AssetPath} answered {(int)asset.StatusCode}; files of the web root are served to anyone.");
        (await asset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(WebRoot.AssetContent);
        var caching = asset.Headers.CacheControl;
        caching.ShouldNotBeNull($"GET {WebRoot.AssetPath} answered no Cache-Control; /assets/* is named by content and is cached for good.");
        caching.MaxAge.ShouldNotBeNull($"GET {WebRoot.AssetPath} answered Cache-Control '{caching}' without max-age.");
        caching.MaxAge.Value.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromDays(365), $"GET {WebRoot.AssetPath} answered Cache-Control '{caching}'; max-age should be a year.");
        caching.Extensions.Select(extension => extension.Name).ShouldContain("immutable", $"GET {WebRoot.AssetPath} answered Cache-Control '{caching}' without immutable.");
        await app.ShouldBeTheAppAsync("A page load of /catalog");
    }

    [Fact]
    public async Task Without_a_web_root_nothing_is_served_and_navigations_reach_the_service()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();

        using var root = await client.SendAsync(Requests.Navigation("/"), TestContext.Current.CancellationToken);
        using var page = await client.SendAsync(Requests.Navigation("/catalog"), TestContext.Current.CancellationToken);

        root.StatusCode.ShouldBe(HttpStatusCode.NotFound, "Without index.html in the web root, a page load of / should answer 404: the dev server serves the app.");
        page.Headers.Contains(ModuleService.MarkerHeader).ShouldBeTrue($"Without index.html in the web root, a page load of /catalog answered {(int)page.StatusCode} without reaching the catalog service.");
        edge.Service.Received.Select(request => request.Path).ShouldBe(["/catalog"]);
    }

    /// <summary>A precondition: the edge serves the app at all, or a test that it does not serve it somewhere proves nothing.</summary>
    private static async Task ShouldServeTheAppAsync(HttpClient client)
    {
        using var response = await client.SendAsync(Requests.Navigation("/"), TestContext.Current.CancellationToken);
        await response.ShouldBeTheAppAsync("Precondition: a page load of /");
    }
}
