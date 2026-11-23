using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The edge holds a body of unknown length only for a caller it has signed in, and only up to
/// <c>Edge:MaxBufferedRequestBodyBytes</c>, 1 MiB by default (docs/adr/0021). The path is one no service maps, so a
/// service that got the body would answer <c>404</c>: a <c>413</c> can only be the edge's.
/// </summary>
public class EdgeBufferedBodyTests(AspireFixture aspire)
{
    private static readonly Uri Unmapped = new("/compliance/edge-buffer-probe", UriKind.Relative);

    [Fact]
    public async Task A_signed_in_callers_body_of_unknown_length_above_the_edges_limit_is_refused_as_too_large_at_the_edge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var body = JsonContent.Create(new { padding = new string('a', 1_100_000) });
        body.Headers.ContentLength.ShouldBeNull();

        using var response = await manager.PostAsync(Unmapped, body, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_signed_in_callers_body_of_unknown_length_within_the_edges_limit_reaches_the_service()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var body = JsonContent.Create(new { padding = new string('a', 100_000) });

        using var response = await manager.PostAsync(Unmapped, body, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_anonymous_callers_body_of_unknown_length_is_not_held_and_is_refused_by_the_service()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = aspire.CreateHostClient();
        using var body = JsonContent.Create(new { padding = new string('a', 1_100_000) });

        using var response = await anonymous.PostAsync(new Uri("/compliance/samples", UriKind.Relative), body, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
