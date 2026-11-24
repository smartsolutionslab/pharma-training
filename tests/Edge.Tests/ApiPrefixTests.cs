using System.Net;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// E1: the web app calls the edge under <c>/api</c> (its generated client prefixes every path), the dev server's proxy and
/// the published edge alike. The edge handles <c>/api/...</c> as the same request without the segment: what it maps itself,
/// and what it forwards to a module's service, which never sees the prefix. A request without <c>/api</c> is unchanged.
/// </summary>
public class ApiPrefixTests
{
    [Fact]
    public async Task The_edge_answers_under_api_as_without_it()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();
        var token = TestTokens.For("developer");

        using var plain = await client.SendAsync(Requests.Data("/me", token), TestContext.Current.CancellationToken);
        using var prefixed = await client.SendAsync(Requests.Data("/api/me", token), TestContext.Current.CancellationToken);

        plain.StatusCode.ShouldBe(HttpStatusCode.OK, "GET /me as developer should answer 200; the test's token is not accepted, so nothing below says anything about /api.");
        var expected = await plain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        prefixed.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET /api/me as developer answered {(int)prefixed.StatusCode}; the edge should answer it as GET /me ({expected}).");
        (await prefixed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(expected, "GET /api/me should answer exactly what GET /me does.");
    }

    [Fact]
    public async Task An_anonymous_caller_is_rejected_under_api_too()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Data("/api/me"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"An anonymous GET /api/me answered {(int)response.StatusCode}; as GET /me, it should be refused with 401.");
    }

    [Fact]
    public async Task A_module_call_under_api_reaches_the_service_without_the_prefix()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();
        var token = TestTokens.For("developer");

        using var response = await client.SendAsync(Requests.Data("/api/catalog/permissions/manage?probe=1", token), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET /api/catalog/permissions/manage answered {(int)response.StatusCode}; the edge should forward it to the catalog service.");
        var received = edge.Service.Received.ShouldHaveSingleItem("The catalog service should receive the call exactly once.");
        received.ShouldBe(
            new ReceivedRequest("GET", "/catalog/permissions/manage", "?probe=1", $"Bearer {token}"),
            "The catalog service should receive the path without /api, with its query and the caller's token.");
    }

    [Fact]
    public async Task A_module_call_without_api_is_forwarded_as_before()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();

        // Precondition: the prefix is stripped, or this test would only repeat today's behaviour.
        using var prefixed = await client.SendAsync(Requests.Data("/api/catalog/samples"), TestContext.Current.CancellationToken);
        prefixed.Headers.Contains(ModuleService.MarkerHeader).ShouldBeTrue($"GET /api/catalog/samples answered {(int)prefixed.StatusCode} without reaching the catalog service; the edge does not strip /api yet.");

        using var plain = await client.SendAsync(Requests.Data("/catalog/samples"), TestContext.Current.CancellationToken);

        plain.Headers.Contains(ModuleService.MarkerHeader).ShouldBeTrue("GET /catalog/samples should still reach the catalog service.");
        edge.Service.Received.Select(request => request.Path).ShouldBe(["/catalog/samples", "/catalog/samples"], "Both calls should reach the service as /catalog/samples.");
    }

    [Fact]
    public async Task An_unknown_path_under_api_is_not_found()
    {
        await using var edge = await EdgeHost.WithoutTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();

        // Precondition: /api is the edge's own prefix, or a 404 below proves nothing.
        using var known = await client.SendAsync(Requests.Data("/api/me"), TestContext.Current.CancellationToken);
        known.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"An anonymous GET /api/me answered {(int)known.StatusCode}, not 401: the edge does not handle /api as its prefix yet.");

        using var unknown = await client.SendAsync(Requests.Data("/api/nothing"), TestContext.Current.CancellationToken);
        using var lookalike = await client.SendAsync(Requests.Data("/apime"), TestContext.Current.CancellationToken);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound, "GET /api/nothing should answer 404.");
        lookalike.StatusCode.ShouldBe(HttpStatusCode.NotFound, "GET /apime should answer 404: only the whole segment /api is the prefix.");
        edge.Service.Received.ShouldBeEmpty("No module's service should receive a path the edge does not route.");
    }
}
