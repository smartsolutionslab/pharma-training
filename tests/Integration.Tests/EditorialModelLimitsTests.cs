using System.Net;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor over the limit on an endpoint that calls the model (#174) gets 429 through the edge, with
/// <c>Retry-After</c> and the module's code <c>editorial.model-limit-reached</c>, while another editor is still served
/// and a caller without a token or the policy is refused as before, never counted. The run sets every limit to
/// <see cref="AspireFixture.EditorialModelLimit"/>; each test spends it as <c>editor-b</c>, whom no other test calls the
/// model as, on its own endpoint, so neither depends on how often other tests call it as <c>editor</c>. This run has no
/// model (ADR 0001), so each counted call answers 503 model-not-configured until the limit takes its place.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialModelLimitsTests(AspireFixture aspire)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public Task An_editor_over_the_content_review_limit_gets_429_with_retry_after_and_the_modules_code() =>
        OverTheLimitAsync(id => new Uri($"/editorial/course-versions/{id}/content-review", UriKind.Relative));

    [Fact]
    public Task An_editor_over_the_test_draft_limit_gets_429_with_retry_after_and_the_modules_code() =>
        OverTheLimitAsync(id => new Uri($"/editorial/course-versions/{id}/test-draft", UriKind.Relative));

    private async Task OverTheLimitAsync(Func<Guid, Uri> route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editorB = await aspire.CreateHostClientAsync("editor-b");

        for (var call = 1; call <= AspireFixture.EditorialModelLimit; call++)
        {
            using var counted = await editorB.PostAsync(route(Guid.CreateVersion7()), null, cancellationToken);
            counted.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, $"call {call} of {AspireFixture.EditorialModelLimit} is within the limit");
        }

        using var refused = await editorB.PostAsync(route(Guid.CreateVersion7()), null, cancellationToken);
        var body = await refused.Content.ReadAsStringAsync(cancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, body);
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        retryAfter.ShouldNotBeNull("Retry-After in seconds");
        retryAfter.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        retryAfter.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromDays(1));
        JsonSerializer.Deserialize<Problem>(body, Web).ShouldNotBeNull().Code.ShouldBe("editorial.model-limit-reached");

        using (var editor = await aspire.CreateHostClientAsync("editor"))
        {
            using var served = await editor.PostAsync(route(Guid.CreateVersion7()), null, cancellationToken);
            served.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, "another editor has a budget of their own");
        }

        using (var anonymous = aspire.CreateHostClient())
        {
            using var unauthorized = await anonymous.PostAsync(route(Guid.CreateVersion7()), null, cancellationToken);
            unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var owner = await aspire.CreateHostClientAsync("owner");
        using var forbidden = await owner.PostAsync(route(Guid.CreateVersion7()), null, cancellationToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record Problem(string Code);
}
