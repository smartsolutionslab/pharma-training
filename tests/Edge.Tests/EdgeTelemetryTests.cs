using System.Net.Http.Headers;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// The edge's spans record no path beyond a request's first segment (#311): the edge knows no module's routes, so it
/// cannot tell a path that carries a credential (Certification's verification code) from any other. Its server span's
/// <c>url.path</c> keeps <c>/api</c> and the module (<c>/api/certification/…</c>), and the span of the call it forwards
/// to the service keeps the module alone in <c>url.full</c>; the service's own span, in the same trace, holds its route.
/// </summary>
public class EdgeTelemetryTests
{
    [Fact]
    public async Task The_edges_spans_keep_the_module_and_never_the_rest_of_the_path()
    {
        var code = Guid.CreateVersion7().ToString("N");
        using var activities = new RecordedActivities();
        await using var edge = await EdgeHost.ServingTheAppAsync("Production");
        using var client = edge.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/verify/{code}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains(ModuleService.MarkerHeader).ShouldBeTrue($"The edge answered {response.StatusCode} itself; the call never reached the module service.");
        activities.Stopped.ShouldContain(activity => (string?)activity.GetTagItem("url.path") == "/api/catalog/…", "The edge's server span does not keep /api and the module.");
        activities.Stopped.ShouldContain(activity => ((string?)activity.GetTagItem("url.full") ?? string.Empty).EndsWith("/catalog/…", StringComparison.Ordinal), "The span of the forwarded call does not keep the module.");
        activities.Stopped.SelectMany(activity => activity.TagObjects).Where(tag => tag.Value != null && tag.Value.ToString()!.Contains(code, StringComparison.Ordinal))
            .ShouldBeEmpty("An edge span carries the rest of the path.");
    }
}
