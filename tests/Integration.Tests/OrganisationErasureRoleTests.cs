using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Requesting and reading a person's erasure (#115) requires the policy organisation.erase-persons, which the realm role
/// support holds: support may, a manager (developer) and a signed-in user without the role (visitor) are forbidden, and an
/// anonymous caller is unauthorized.
/// </summary>
public class OrganisationErasureRoleTests(AspireFixture aspire)
{
    private static readonly Uri Erasures = new("/organisation/erasures", UriKind.Relative);

    [Fact]
    public async Task Support_may_request_an_erasure()
    {
        using var client = await aspire.CreateHostClientAsync("support");

        using var response = await client.PostAsJsonAsync(Erasures, new { personId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldStartWith("/organisation/erasures/");
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("visitor")]
    public async Task A_signed_in_user_without_the_support_role_may_not_request_an_erasure(string user)
    {
        using var client = await aspire.CreateHostClientAsync(user);

        using var response = await client.PostAsJsonAsync(Erasures, new { personId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_may_not_request_an_erasure()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.PostAsJsonAsync(Erasures, new { personId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Support_may_read_an_erasure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("support");
        using var created = await client.PostAsJsonAsync(Erasures, new { personId = Guid.CreateVersion7() }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(cancellationToken));

        using var response = await client.GetAsync(created.Headers.Location, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("visitor")]
    public async Task A_signed_in_user_without_the_support_role_may_not_read_an_erasure(string user)
    {
        using var client = await aspire.CreateHostClientAsync(user);

        using var response = await client.GetAsync(ErasureUri(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_may_not_read_an_erasure()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(ErasureUri(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Support_reading_an_unknown_erasure_is_told_it_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("support");

        using var response = await client.GetAsync(ErasureUri(Guid.CreateVersion7()), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken)).ShouldContain("organisation.erasure-not-found");
    }

    private static Uri ErasureUri(Guid id) => new($"/organisation/erasures/{id}", UriKind.Relative);
}
