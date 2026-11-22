using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The invited person accepts an invitation through the running application, with the token from the link, as Keycloak
/// knows them; and the database backs the rule the aggregate checks: a person is active at most once per pharmacy.
/// </summary>
public class OrganisationAcceptInvitationTests(AspireFixture aspire)
{
    private const string UniqueViolation = "23505";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private static readonly Uri Accept = new("/organisation/invitations/accept", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task The_database_refuses_a_second_active_membership_of_one_person_in_one_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var otherPharmacy = await RegisterAsync(owner, cancellationToken);
        var person = Guid.CreateVersion7();
        await database.InsertMembershipOfPersonAsync(pharmacy, person, Address(), "Ended", cancellationToken);
        await database.InsertMembershipOfPersonAsync(pharmacy, person, Address(), "Active", cancellationToken);
        await database.InsertMembershipOfPersonAsync(otherPharmacy, person, Address(), "Active", cancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => database.InsertMembershipOfPersonAsync(pharmacy, person, Address(), "Active", cancellationToken));

        refused.SqlState.ShouldBe(UniqueViolation);
    }

    [Fact]
    public async Task The_invited_person_accepts_and_is_an_active_member_with_the_name_and_subject_from_keycloak()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var token = Token();
        var membership = await database.InsertInvitationAsync(pharmacy, await database.EmailOfAsync("employee-a"), HashOf(token), "Invited", cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var accepted = await AcceptAsync(employee, token, cancellationToken);

        await ShouldAnswerAsync(accepted, HttpStatusCode.OK, cancellationToken);
        (await accepted.Content.ReadFromJsonAsync<Guid>(cancellationToken)).ShouldBe(pharmacy);
        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Active");
        stored.Person.ShouldBe(await database.SubjectOfAsync("employee-a"));
        stored.Name.ShouldBe("Anna Employee");
        stored.TokenHash.ShouldBeNull();
        var mine = await employee.GetFromJsonAsync<MyPharmacy>(new Uri($"/organisation/pharmacies/{pharmacy}", UriKind.Relative), cancellationToken);
        mine.ShouldNotBeNull().MembershipId.ShouldBe(membership);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"token":null}""")]
    [InlineData("""{"token":""}""")]
    [InlineData("""{"token":"   "}""")]
    [InlineData("""{"token":"short"}""")]
    [InlineData("""{"token":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    public async Task A_body_without_a_token_that_could_be_one_is_the_callers_mistake(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var refused = await employee.PostAsync(Accept, content, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, "organisation.invalid-invitation-token", cancellationToken);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_accept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var token = Token();
        var membership = await database.InsertInvitationAsync(pharmacy, await database.EmailOfAsync("employee-a"), HashOf(token), "Invited", cancellationToken);
        using var anonymous = aspire.CreateHostClient();

        using var refused = await AcceptAsync(anonymous, token, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await database.MemberAsync(membership, cancellationToken)).Status.ShouldBe("Invited");
    }

    /// <summary>A fresh address per row, so the one-row-per-address index never decides for the person index.</summary>
    private static string Address() => $"person-{Guid.CreateVersion7()}@example.com";

    /// <summary>A token as the mail's link carries one: 32 random bytes, 43 URL-safe characters.</summary>
    private static string Token() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The SHA-256 of the token in lower-case hex, as the row keeps it.</summary>
    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(Accept, new { token }, cancellationToken);

    private static async Task ShouldAnswerAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken cancellationToken) =>
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(cancellationToken));

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        problem.ShouldNotBeNull($"{(int)response.StatusCode} without a body").Code.ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private sealed record MyPharmacy(Guid Id, Guid MembershipId);

    private sealed record Problem(string? Code);
}
