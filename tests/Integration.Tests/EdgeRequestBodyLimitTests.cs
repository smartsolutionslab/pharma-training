using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A body larger than its route's limit, sent through the edge, is answered with the service's <c>413</c> every time,
/// never with the edge's <c>502</c> (#214): with a <c>Content-Length</c> and with a body of unknown length alike. The
/// route is Compliance's <c>POST /compliance/pharmacies/{id}/assignments</c>, limited to 65 536 bytes; it refuses the
/// body before its handler runs, so no pharmacy or course is needed.
/// </summary>
public class EdgeRequestBodyLimitTests(AspireFixture aspire)
{
    // The issue's bar: fifty refusals in a row, none of them a 502.
    private const int Attempts = 50;

    // About 3.9 MB, some sixty times the route's limit, so the race the edge loses without the fix is lost nearly every time.
    private const int PersonIds = 100_000;

    [Fact]
    public async Task A_body_with_a_length_larger_than_its_routes_limit_is_refused_as_too_large_every_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");
        var payload = JsonSerializer.Serialize(OversizedRequest());
        List<HttpStatusCode> statuses = [];

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await manager.PostAsync(AssignmentsUri(), body, cancellationToken);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldAllBe(status => status == HttpStatusCode.RequestEntityTooLarge, Counts(statuses));
    }

    [Fact]
    public async Task A_body_of_unknown_length_larger_than_its_routes_limit_is_refused_as_too_large_every_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");
        var payload = OversizedRequest();
        List<HttpStatusCode> statuses = [];

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var body = JsonContent.Create(payload);
            if (attempt == 0)
            {
                body.Headers.ContentLength.ShouldBeNull();
            }

            using var response = await manager.PostAsync(AssignmentsUri(), body, cancellationToken);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldAllBe(status => status == HttpStatusCode.RequestEntityTooLarge, Counts(statuses));
    }

    private static object OversizedRequest() =>
        new
        {
            courseId = Guid.CreateVersion7(),
            personIds = Enumerable.Range(0, PersonIds).Select(_ => Guid.CreateVersion7()).ToArray(),
            dueDate = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(30),
        };

    private static Uri AssignmentsUri() => new($"/compliance/pharmacies/{Guid.CreateVersion7()}/assignments", UriKind.Relative);

    private static string Counts(List<HttpStatusCode> statuses) =>
        string.Join(", ", statuses.GroupBy(status => (int)status).OrderBy(group => group.Key).Select(group => $"{group.Key}x{group.Count()}"));
}
