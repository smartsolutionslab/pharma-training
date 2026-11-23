using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The invited person accepts an invitation through the running application, with the token from the link, as Keycloak
/// knows them; and the database backs the rule the aggregate checks: a person is active at most once per pharmacy.
/// </summary>
public partial class OrganisationAcceptInvitationTests(AspireFixture aspire)
{
    private const string UniqueViolation = "23505";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private static readonly Uri Accept = new("/organisation/invitations/accept", UriKind.Relative);

    private static readonly Uri Mine = new("/organisation/pharmacies/mine?page=1&pageSize=100", UriKind.Relative);

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire);

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
        stored.TokenHashes.ShouldBeEmpty();
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

    [Fact]
    public async Task The_invited_person_accepts_with_the_token_from_the_real_mail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var name = $"Apotheke {Guid.CreateVersion7()}";
        var pharmacy = await RegisterAsync(owner, name, cancellationToken);
        using var invited = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = "employee-b@example.com", jobFunction = "Pta", role = "Employee" }, cancellationToken);
        await ShouldAnswerAsync(invited, HttpStatusCode.Created, cancellationToken);
        var membership = await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        var token = TokenIn(await mailpit.MessageToAsync("employee-b@example.com", name, cancellationToken));
        using var employee = await aspire.CreateHostClientAsync("employee-b");

        using var accepted = await AcceptAsync(employee, token, cancellationToken);

        await ShouldAnswerAsync(accepted, HttpStatusCode.OK, cancellationToken);
        (await accepted.Content.ReadFromJsonAsync<Guid>(cancellationToken)).ShouldBe(pharmacy);
        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Active");
        stored.Person.ShouldBe(await database.SubjectOfAsync("employee-b"));
        stored.Name.ShouldBe("Ben Employee");
        stored.TokenHashes.ShouldBeEmpty();
        var mine = await employee.GetFromJsonAsync<MyPharmacyPage>(Mine, cancellationToken);
        mine.ShouldNotBeNull().Items.Select(item => item.Id).ShouldContain(pharmacy);
    }

    [Fact]
    public async Task A_used_link_is_not_found_and_answers_like_an_unknown_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, _, token) = await InvitedAsync("employee-a", cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        using (var first = await AcceptAsync(employee, token, cancellationToken))
        {
            await ShouldAnswerAsync(first, HttpStatusCode.OK, cancellationToken);
        }

        using var again = await AcceptAsync(employee, token, cancellationToken);
        using var unknown = await AcceptAsync(employee, Token(), cancellationToken);

        await ShouldBeRefusedAsync(again, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
        await ShouldBeRefusedAsync(unknown, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
        var againBody = await again.Content.ReadAsStringAsync(cancellationToken);
        var unknownBody = await unknown.Content.ReadAsStringAsync(cancellationToken);
        Detail(againBody).ShouldBe(Detail(unknownBody));
        againBody.ShouldNotContain(pharmacy.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task An_unknown_link_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var refused = await AcceptAsync(employee, Token(), cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_invitation_that_ran_out_is_gone_and_stays_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, membership, token) = await InvitedAsync("employee-a", cancellationToken, expiresAt: TimeProvider.System.GetUtcNow().AddMinutes(-1));
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var refused = await AcceptAsync(employee, token, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Gone, "organisation.invitation-expired", cancellationToken);
        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Invited");
        stored.TokenHashes.ShouldBe([HashOf(token)]);
    }

    [Fact]
    public async Task A_forwarded_link_is_forbidden_and_the_invited_person_can_still_accept_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, membership, token) = await InvitedAsync("employee-a", cancellationToken);
        using var forwardedTo = await aspire.CreateHostClientAsync("employee-b");
        using var invitee = await aspire.CreateHostClientAsync("employee-a");

        using var forwarded = await AcceptAsync(forwardedTo, token, cancellationToken);

        await ShouldBeRefusedAsync(forwarded, HttpStatusCode.Forbidden, "organisation.invitation-for-another-address", cancellationToken);
        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Invited");
        stored.Person.ShouldBeNull();
        stored.TokenHashes.ShouldBe([HashOf(token)]);
        using var accepted = await AcceptAsync(invitee, token, cancellationToken);
        await ShouldAnswerAsync(accepted, HttpStatusCode.OK, cancellationToken);
        (await accepted.Content.ReadFromJsonAsync<Guid>(cancellationToken)).ShouldBe(pharmacy);
    }

    [Fact]
    public async Task A_revoked_invitations_link_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var token = Token();
        var membership = await database.InsertInvitationAsync(pharmacy, "employee-a@example.com", HashOf(token), "Invited", cancellationToken);
        using (var revoked = await owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken))
        {
            revoked.IsSuccessStatusCode.ShouldBeTrue(await revoked.Content.ReadAsStringAsync(cancellationToken));
        }

        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var refused = await AcceptAsync(employee, token, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
        (await database.MemberAsync(membership, cancellationToken)).Status.ShouldBe("Revoked");
    }

    [Fact]
    public async Task A_renewed_invitations_old_link_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var token = Token();
        var membership = await database.InsertInvitationAsync(pharmacy, "employee-a@example.com", HashOf(token), "Invited", cancellationToken);
        using (var renewed = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken))
        {
            await ShouldAnswerAsync(renewed, HttpStatusCode.OK, cancellationToken);
        }

        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var refused = await AcceptAsync(employee, token, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
        (await database.MemberAsync(membership, cancellationToken)).Status.ShouldBe("Invited");
    }

    [Fact]
    public async Task An_account_whose_address_keycloak_has_not_verified_cannot_accept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, membership, token) = await InvitedAsync("unverified", cancellationToken, address: "unverified@example.com");
        using var unverified = await aspire.CreateHostClientAsync("unverified");

        using var refused = await AcceptAsync(unverified, token, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Forbidden, "organisation.email-not-verified", cancellationToken);
        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Invited");
        stored.Person.ShouldBeNull();
        stored.TokenHashes.ShouldBe([HashOf(token)]);
    }

    [Fact]
    public async Task One_person_accepts_invitations_of_two_pharmacies_and_is_active_in_both()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (first, firstMembership, firstToken) = await InvitedAsync("employee-a", cancellationToken);
        var (second, secondMembership, secondToken) = await InvitedAsync("employee-a", cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var acceptedFirst = await AcceptAsync(employee, firstToken, cancellationToken);
        using var acceptedSecond = await AcceptAsync(employee, secondToken, cancellationToken);

        await ShouldAnswerAsync(acceptedFirst, HttpStatusCode.OK, cancellationToken);
        await ShouldAnswerAsync(acceptedSecond, HttpStatusCode.OK, cancellationToken);
        var person = await database.SubjectOfAsync("employee-a");
        var active = (await database.ActiveMembershipsOfAsync(person, cancellationToken))
            .Where(membership => membership.PharmacyId == first || membership.PharmacyId == second)
            .ToList();
        active.Select(membership => membership.Id).ShouldBe([firstMembership, secondMembership], ignoreOrder: true);
        (await employee.GetFromJsonAsync<MyPharmacy>(PharmacyUri(first), cancellationToken)).ShouldNotBeNull().MembershipId.ShouldBe(firstMembership);
        (await employee.GetFromJsonAsync<MyPharmacy>(PharmacyUri(second), cancellationToken)).ShouldNotBeNull().MembershipId.ShouldBe(secondMembership);
    }

    [Fact]
    public async Task A_person_already_active_in_the_pharmacy_under_another_address_is_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, membership, token) = await InvitedAsync("employee-a", cancellationToken);
        await database.InsertMembershipOfPersonAsync(pharmacy, await database.SubjectOfAsync("employee-a"), Address(), "Active", cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var refused = await AcceptAsync(employee, token, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Conflict, "organisation.person-already-member", cancellationToken);
        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Invited");
        stored.TokenHashes.ShouldBe([HashOf(token)]);
    }

    [Fact]
    public async Task Of_two_accepts_of_one_link_at_once_one_succeeds_and_the_other_is_told_the_invitation_changed_without_the_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, membership, token) = await InvitedAsync("employee-a", cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            var one = AcceptAsync(employee, token, cancellationToken);
            var other = AcceptAsync(employee, token, cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(one, other);
        }

        try
        {
            var described = await DescribeAsync(responses, cancellationToken);
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true, described);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            await ShouldBeRefusedAsync(lost, HttpStatusCode.Conflict, "organisation.invitation-changed", cancellationToken);
            (await lost.Content.ReadAsStringAsync(cancellationToken)).ShouldNotContain(pharmacy.ToString(), Case.Insensitive);
            var person = await database.SubjectOfAsync("employee-a");
            (await database.ActiveMembershipsOfAsync(person, cancellationToken))
                .Where(active => active.PharmacyId == pharmacy)
                .Select(active => active.Id)
                .ShouldBe([membership]);
            using var retry = await AcceptAsync(employee, token, cancellationToken);
            await ShouldBeRefusedAsync(retry, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
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
    public async Task An_accept_that_loses_the_race_to_a_revoke_is_told_the_invitation_changed_without_the_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, membership, token) = await InvitedAsync("employee-a", cancellationToken);
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        var (firstResponse, secondResponse) = await FirstThenSecondAsync(
            pharmacy,
            () => owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken),
            () => AcceptAsync(employee, token, cancellationToken),
            cancellationToken);
        using var revoke = firstResponse;
        using var accept = secondResponse;
        revoke.IsSuccessStatusCode.ShouldBeTrue(await revoke.Content.ReadAsStringAsync(cancellationToken));
        await ShouldBeRefusedAsync(accept, HttpStatusCode.Conflict, "organisation.invitation-changed", cancellationToken);
        (await accept.Content.ReadAsStringAsync(cancellationToken)).ShouldNotContain(pharmacy.ToString(), Case.Insensitive);

        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Revoked");
        stored.Person.ShouldBeNull();
        using var retry = await AcceptAsync(employee, token, cancellationToken);
        await ShouldBeRefusedAsync(retry, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_accept_that_loses_the_race_to_a_renewal_is_told_the_invitation_changed_without_the_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, membership, token) = await InvitedAsync("employee-a", cancellationToken);
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        var (firstResponse, secondResponse) = await FirstThenSecondAsync(
            pharmacy,
            () => owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken),
            () => AcceptAsync(employee, token, cancellationToken),
            cancellationToken);
        using var renew = firstResponse;
        using var accept = secondResponse;
        await ShouldAnswerAsync(renew, HttpStatusCode.OK, cancellationToken);
        await ShouldBeRefusedAsync(accept, HttpStatusCode.Conflict, "organisation.invitation-changed", cancellationToken);
        (await accept.Content.ReadAsStringAsync(cancellationToken)).ShouldNotContain(pharmacy.ToString(), Case.Insensitive);

        var stored = await database.MemberAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Invited");
        stored.Person.ShouldBeNull();
        using var retry = await AcceptAsync(employee, token, cancellationToken);
        await ShouldBeRefusedAsync(retry, HttpStatusCode.NotFound, "organisation.invitation-not-found", cancellationToken);
    }

    [Fact]
    public async Task Compliance_stores_the_accepted_membership_as_a_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (pharmacy, membership, token) = await InvitedAsync("employee-a", cancellationToken, role: "Employee", jobFunction: "Pka");
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var accepted = await AcceptAsync(employee, token, cancellationToken);

        await ShouldAnswerAsync(accepted, HttpStatusCode.OK, cancellationToken);
        var member = (await ComplianceMembersAsync(membership, atLeast: 1, cancellationToken)).ShouldHaveSingleItem();
        member.PersonId.ShouldBe(await database.SubjectOfAsync("employee-a"));
        member.PharmacyId.ShouldBe(pharmacy);
        member.DisplayName.ShouldBe("Anna Employee");
        member.JobFunction.ShouldBe("Pka");
        member.Role.ShouldBe("Employee");
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

    private static Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken) =>
        RegisterAsync(client, $"Apotheke {Guid.CreateVersion7()}", cancellationToken);

    private static async Task<Guid> RegisterAsync(HttpClient client, string name, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name, jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static Uri InvitationsUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative);

    private static Uri InvitationUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}", UriKind.Relative);

    private static Uri RenewUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}/renew", UriKind.Relative);

    private static Uri PharmacyUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}", UriKind.Relative);

    private static string? Detail(string body)
    {
        using var problem = JsonDocument.Parse(body);
        return problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
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

    [GeneratedRegex("#token=(?<token>[A-Za-z0-9_-]{43})$")]
    private static partial Regex LinkToken();

    /// <summary>
    /// Registers a pharmacy as <c>owner</c> and inserts, past the API, an open invitation of the realm user's address
    /// with a token only this test knows. No mail is requested for it, so nothing replaces its hash.
    /// </summary>
    private async Task<(Guid Pharmacy, Guid Membership, string Token)> InvitedAsync(
        string user,
        CancellationToken cancellationToken,
        string? address = null,
        DateTimeOffset? expiresAt = null,
        string role = "Employee",
        string jobFunction = "Pta")
    {
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var token = Token();
        var email = address ?? await database.EmailOfAsync(user);
        var membership = await database.InsertInvitationAsync(pharmacy, email, HashOf(token), "Invited", cancellationToken, expiresAt, role, jobFunction);
        return (pharmacy, membership, token);
    }

    /// <summary>
    /// Starts the first request and waits until it waits for the lock on the pharmacy, having loaded it; then the second,
    /// which loads the same pharmacy and queues behind the first. PostgreSQL grants a row's lock in the order its waiters
    /// asked, so once the gate commits the first saves first and the second loses the race on the row version.
    /// </summary>
    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> FirstThenSecondAsync(
        Guid pharmacy,
        Func<Task<HttpResponseMessage>> first,
        Func<Task<HttpResponseMessage>> second,
        CancellationToken cancellationToken)
    {
        await using var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken);
        var one = first();
        await database.WaitUntilBlockedAsync(gate, 1, cancellationToken);
        var other = second();
        await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
        await gate.CommitAsync(cancellationToken);
        return (await one, await other);
    }

    /// <summary>The token from the mail's link, which leads to the web app's accept page with the token in the fragment.</summary>
    private string TokenIn(Mailpit.CaughtMail mail)
    {
        var accept = $"{aspire.App.GetEndpoint("web", "http").AbsoluteUri.TrimEnd('/')}/invitations/accept#token=";
        var line = mail.Text.ReplaceLineEndings("\n").Split('\n').Select(candidate => candidate.Trim()).SingleOrDefault(candidate => candidate.Contains("#token=", StringComparison.Ordinal));
        line.ShouldNotBeNull($"the mail holds no link with a token: {mail.Text}");
        line.ShouldStartWith(accept, Case.Sensitive);
        var match = LinkToken().Match(line);
        match.Success.ShouldBeTrue($"the link's token is not 43 URL-safe characters: {line}");
        return match.Groups["token"].Value;
    }

    /// <summary>Polls Compliance's members for the membership until at least the given number of rows exist, or the wait ends.</summary>
    private async Task<List<ComplianceMember>> ComplianceMembersAsync(Guid membership, int atLeast, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            List<ComplianceMember> rows = [];
            await using (var command = new NpgsqlCommand("select person_id, pharmacy_id, display_name, job_function, role from compliance.members where id = @id", connection))
            {
                command.Parameters.AddWithValue("id", membership);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new ComplianceMember(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
                }
            }

            if (rows.Count >= atLeast || TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                rows.Count.ShouldBeGreaterThanOrEqualTo(atLeast, $"Compliance had not stored the membership {membership} after {WaitTimeout.TotalSeconds:0} s");
                return rows;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private sealed record MyPharmacy(Guid Id, Guid MembershipId);

    private sealed record MyPharmacyPage(IReadOnlyList<MyPharmacy> Items);

    private sealed record ComplianceMember(Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role);

    private sealed record Problem(string? Code);
}
