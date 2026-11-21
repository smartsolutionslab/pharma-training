using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A pharmacy's management invites an address, renews and revokes the invitation through the running application,
/// and the database backs the rules the aggregate checks: one invited-or-active row per address, one row per token hash.
/// </summary>
public class OrganisationInvitationsTests(AspireFixture aspire)
{
    private const string UniqueViolation = "23505";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task The_database_refuses_a_second_open_invitation_of_one_address()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var otherPharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        await database.InsertInvitationAsync(pharmacy, address, Hash(), "Invited", cancellationToken);
        await database.InsertInvitationAsync(pharmacy, address, Hash(), "Revoked", cancellationToken);
        await database.InsertInvitationAsync(otherPharmacy, address, Hash(), "Invited", cancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => database.InsertInvitationAsync(pharmacy, address, Hash(), "Invited", cancellationToken));

        refused.SqlState.ShouldBe(UniqueViolation);
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Invited", "Revoked"]);
    }

    [Fact]
    public async Task The_database_refuses_a_second_row_with_one_token_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var hash = Hash();
        await database.InsertInvitationAsync(pharmacy, Invitee(), hash, "Invited", cancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => database.InsertInvitationAsync(pharmacy, Invitee(), hash, "Invited", cancellationToken));

        refused.SqlState.ShouldBe(UniqueViolation);
    }

    [Fact]
    public async Task A_manager_invites_an_address_and_the_invitation_is_stored_as_invited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();

        using var invited = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pka", role = "Manager" }, cancellationToken);

        await ShouldAnswerAsync(invited, HttpStatusCode.Created, cancellationToken);
        var membership = await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        invited.Headers.Location.ShouldBe(new Uri($"/organisation/pharmacies/{pharmacy}/invitations/{membership}", UriKind.Relative));
        var stored = await database.MembershipAsync(membership, cancellationToken);
        (stored.Status, stored.Role, stored.JobFunction).ShouldBe(("Invited", "Manager", "Pka"));
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Invited"]);
    }

    [Fact]
    public async Task An_address_already_invited_and_the_managers_own_address_are_refused_as_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        await InviteAsync(owner, pharmacy, address, cancellationToken);

        using var again = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address.ToUpperInvariant(), jobFunction = "Pta", role = "Employee" }, cancellationToken);
        using var own = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = await database.EmailOfAsync("owner"), jobFunction = "Pta", role = "Employee" }, cancellationToken);

        await ShouldBeRefusedAsync(again, HttpStatusCode.Conflict, "organisation.already-member-or-invited", cancellationToken);
        await ShouldBeRefusedAsync(own, HttpStatusCode.Conflict, "organisation.already-member-or-invited", cancellationToken);
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Invited"]);
    }

    [Fact]
    public async Task A_manager_renews_an_invitation_and_its_token_hash_is_replaced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);
        var before = await database.TokenHashAsync(membership, cancellationToken);

        using var renewed = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);

        await ShouldAnswerAsync(renewed, HttpStatusCode.OK, cancellationToken);
        var after = await database.TokenHashAsync(membership, cancellationToken);
        before.ShouldNotBeNull();
        after.ShouldNotBeNull();
        after.ShouldNotBe(before);
        (await database.MembershipAsync(membership, cancellationToken)).Status.ShouldBe("Invited");
    }

    [Fact]
    public async Task A_revoked_invitation_keeps_no_token_hash_cannot_be_renewed_and_frees_the_address()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);

        using var revoked = await owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);

        await ShouldAnswerAsync(revoked, HttpStatusCode.OK, cancellationToken);
        (await database.MembershipAsync(membership, cancellationToken)).Status.ShouldBe("Revoked");
        (await database.TokenHashAsync(membership, cancellationToken)).ShouldBeNull();
        using var renewed = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
        await ShouldBeRefusedAsync(renewed, HttpStatusCode.NotFound, "organisation.no-open-invitation", cancellationToken);
        using var revokedAgain = await owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);
        await ShouldBeRefusedAsync(revokedAgain, HttpStatusCode.NotFound, "organisation.no-open-invitation", cancellationToken);
        await InviteAsync(owner, pharmacy, address, cancellationToken);
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Revoked", "Invited"]);
    }

    [Fact]
    public async Task A_membership_that_is_no_open_invitation_of_the_pharmacy_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var otherPharmacy = await RegisterAsync(owner, cancellationToken);
        var elsewhere = await InviteAsync(owner, otherPharmacy, Invitee(), cancellationToken);
        var own = await MembershipOfAsync(owner, pharmacy, cancellationToken);
        List<Guid> notOpenHere = [Guid.Empty, Guid.CreateVersion7(), own, elsewhere];

        foreach (var membership in notOpenHere)
        {
            using var renewed = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
            using var revoked = await owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);

            await ShouldBeRefusedAsync(renewed, HttpStatusCode.NotFound, "organisation.no-open-invitation", cancellationToken);
            await ShouldBeRefusedAsync(revoked, HttpStatusCode.NotFound, "organisation.no-open-invitation", cancellationToken);
        }

        (await database.MembershipAsync(elsewhere, cancellationToken)).Status.ShouldBe("Invited");
        (await database.MembershipAsync(own, cancellationToken)).Status.ShouldBe("Active");
    }

    [Fact]
    public async Task An_employee_may_neither_invite_nor_renew_nor_revoke()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);
        var hash = await database.TokenHashAsync(membership, cancellationToken);
        var address = Invitee();

        using var invited = await employee.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);
        using var renewed = await employee.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
        using var revoked = await employee.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);

        await ShouldBeRefusedAsync(invited, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
        await ShouldBeRefusedAsync(renewed, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
        await ShouldBeRefusedAsync(revoked, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBeEmpty();
        (await database.MembershipAsync(membership, cancellationToken)).Status.ShouldBe("Invited");
        (await database.TokenHashAsync(membership, cancellationToken)).ShouldBe(hash);
    }

    [Fact]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);

        using var invited = await visitor.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = Invitee(), jobFunction = "Pta", role = "Employee" }, cancellationToken);
        using var renewed = await visitor.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
        using var revoked = await visitor.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);

        await ShouldBeRefusedAsync(invited, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        await ShouldBeRefusedAsync(renewed, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        await ShouldBeRefusedAsync(revoked, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        (await database.MembershipAsync(membership, cancellationToken)).Status.ShouldBe("Invited");
    }

    [Fact]
    public async Task The_empty_pharmacy_id_is_no_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);

        using var invited = await owner.PostAsJsonAsync(InvitationsUri(Guid.Empty), new { email = Invitee(), jobFunction = "Pta", role = "Employee" }, cancellationToken);
        using var renewed = await owner.PostAsync(RenewUri(Guid.Empty, membership), content: null, cancellationToken);
        using var revoked = await owner.DeleteAsync(InvitationUri(Guid.Empty, membership), cancellationToken);

        await ShouldBeRefusedAsync(invited, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        await ShouldBeRefusedAsync(renewed, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        await ShouldBeRefusedAsync(revoked, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_anonymous_caller_neither_invites_nor_renews_nor_revokes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var anonymous = aspire.CreateHostClient();
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);

        using var invited = await anonymous.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = Invitee(), jobFunction = "Pta", role = "Employee" }, cancellationToken);
        using var renewed = await anonymous.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
        using var revoked = await anonymous.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);

        invited.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        renewed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        revoked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("\"email\":\"kein-at\",\"jobFunction\":\"Pta\",\"role\":\"Employee\"", "organisation.invalid-email")]
    [InlineData("\"email\":\"   \",\"jobFunction\":\"Pta\",\"role\":\"Employee\"", "organisation.invalid-email")]
    [InlineData("\"jobFunction\":\"Pta\",\"role\":\"Employee\"", "organisation.invalid-email")]
    [InlineData("\"email\":\"invitee@example.com\",\"jobFunction\":\"Pta\",\"role\":42", "organisation.invalid-membership-role")]
    [InlineData("\"email\":\"invitee@example.com\",\"jobFunction\":42,\"role\":\"Employee\"", "organisation.invalid-job-function")]
    public async Task An_invitation_with_an_address_role_or_job_function_that_is_no_valid_one_is_the_callers_mistake(string fields, string code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        using var body = new StringContent($"{{{fields}}}", Encoding.UTF8, "application/json");

        using var refused = await owner.PostAsync(InvitationsUri(pharmacy), body, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, code, cancellationToken);
    }

    [Fact]
    public async Task An_address_longer_than_254_characters_is_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = $"{new string('a', 255 - "@example.com".Length)}@example.com";

        using var refused = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, "organisation.invalid-email", cancellationToken);
    }

    [Theory]
    [InlineData("\"email\":\"invitee@example.com\",\"jobFunction\":\"Pta\"")]
    [InlineData("\"email\":\"invitee@example.com\",\"role\":\"Employee\"")]
    public async Task An_invitation_without_a_role_or_job_function_is_refused_when_the_body_is_read(string fields)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        using var body = new StringContent($"{{{fields}}}", Encoding.UTF8, "application/json");

        using var refused = await owner.PostAsync(InvitationsUri(pharmacy), body, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Of_two_invitations_of_one_address_at_once_one_is_stored_and_the_other_is_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            var first = owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);
            var second = owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pka", role = "Employee" }, cancellationToken);
            await database.WaitUntilBlockedAsync(2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(first, second);
        }

        try
        {
            var described = await DescribeAsync(responses, cancellationToken);
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true, described);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await CodeOfAsync(lost, cancellationToken)).ShouldBeOneOf(["organisation.pharmacy-changed", "already-exists"], described);
            (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Invited"]);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Of_renewing_and_revoking_one_invitation_at_once_one_succeeds_and_the_other_is_told_the_pharmacy_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            var renew = owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
            var revoke = owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken);
            await database.WaitUntilBlockedAsync(2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(renew, revoke);
        }

        try
        {
            var described = await DescribeAsync(responses, cancellationToken);
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true, described);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            await ShouldBeRefusedAsync(lost, HttpStatusCode.Conflict, "organisation.pharmacy-changed", cancellationToken);
            var stored = await database.MembershipAsync(membership, cancellationToken);
            stored.Status.ShouldBe(responses[0].StatusCode == HttpStatusCode.OK ? "Invited" : "Revoked");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>A fresh address per test, so no test sees another's invitation or mail.</summary>
    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    /// <summary>A token hash as the domain stores one: 64 lower-case hex characters.</summary>
    private static string Hash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static Uri InvitationsUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative);

    private static Uri InvitationUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}", UriKind.Relative);

    private static Uri RenewUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}/renew", UriKind.Relative);

    /// <summary>Invites the address as an employee through the API and answers the new membership's id.</summary>
    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);
        await ShouldAnswerAsync(invited, HttpStatusCode.Created, cancellationToken);
        return await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> MembershipOfAsync(HttpClient client, Guid pharmacy, CancellationToken cancellationToken)
    {
        var read = await client.GetFromJsonAsync<MyPharmacy>(new Uri($"/organisation/pharmacies/{pharmacy}", UriKind.Relative), cancellationToken);
        return read!.MembershipId;
    }

    private static async Task ShouldAnswerAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken cancellationToken) =>
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(cancellationToken));

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        (await CodeOfAsync(response, cancellationToken)).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web)?.Code;
    }

    private static async Task<string> DescribeAsync(HttpResponseMessage[] responses, CancellationToken cancellationToken)
    {
        List<string> answers = [];
        foreach (var response in responses)
        {
            answers.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        return string.Join(" | ", answers);
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
