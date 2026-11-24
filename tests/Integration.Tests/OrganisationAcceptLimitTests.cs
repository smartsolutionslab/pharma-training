using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A user over the accept limit (docs/adr/0004, docs/adr/0020) gets 429 through the edge, with <c>Retry-After</c> and
/// the module's code <c>organisation.accept-limit-reached</c>, while another user is still served and a caller without a
/// token is refused as before. The run raises the per-minute limit to
/// <see cref="AspireFixture.OrganisationAcceptsPerMinute"/>; the test spends it as <c>visitor</c>, whom no other test
/// accepts as, with well-formed tokens no invitation holds, which each answer something other than 429 until the limit
/// takes its place.
/// </summary>
public class OrganisationAcceptLimitTests(AspireFixture aspire)
{
    private static readonly Uri Accept = new("/organisation/invitations/accept", UriKind.Relative);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_user_over_the_accept_limit_gets_429_with_retry_after_and_the_modules_code_while_another_user_is_served()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        for (var accept = 1; accept <= AspireFixture.OrganisationAcceptsPerMinute; accept++)
        {
            using var counted = await visitor.PostAsJsonAsync(Accept, Body(), cancellationToken);
            counted.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, $"accept {accept} of {AspireFixture.OrganisationAcceptsPerMinute} is within the limit");
        }

        using var refused = await visitor.PostAsJsonAsync(Accept, Body(), cancellationToken);
        var body = await refused.Content.ReadAsStringAsync(cancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, body);
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        retryAfter.ShouldNotBeNull("Retry-After in seconds");
        retryAfter.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        retryAfter.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        JsonSerializer.Deserialize<Problem>(body, Web).ShouldNotBeNull().Code.ShouldBe("organisation.accept-limit-reached");

        using (var employee = await aspire.CreateHostClientAsync("employee-b"))
        {
            using var served = await employee.PostAsJsonAsync(Accept, Body(), cancellationToken);
            served.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, "another user has a limit of their own");
        }

        using var anonymous = aspire.CreateHostClient();
        using var unauthorized = await anonymous.PostAsJsonAsync(Accept, Body(), cancellationToken);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static object Body() => new { token = RandomNumberGenerator.GetHexString(64) };

    private sealed record Problem(string Code);
}
