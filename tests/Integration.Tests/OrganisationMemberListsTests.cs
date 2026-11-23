using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A pharmacy's management lists its active members by name and its invitations still open or run out by expiry,
/// a page at a time, through the running application; nobody else may. Second members are seeded in the database.
/// </summary>
public class OrganisationMemberListsTests(AspireFixture aspire)
{
    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task A_manager_lists_the_active_members_by_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownMembership = await MembershipOfAsync(owner, pharmacy, cancellationToken);
        var anna = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        var ben = await database.SeedMemberAsync(pharmacy, "employee-b", "Employee", "Pka", cancellationToken);
        await database.InsertInvitationAsync(pharmacy, Invitee(), TokenHash(), "Invited", cancellationToken);

        var listed = await owner.GetFromJsonAsync<Paged<Member>>(ListUri(pharmacy, "members"), cancellationToken);

        listed.ShouldNotBeNull();
        listed.Items.Select(member => member.MembershipId).ShouldBe([anna, ben, ownMembership]);
        listed.TotalCount.ShouldBe(3);
        listed.Page.ShouldBe(1);
        var now = TimeProvider.System.GetUtcNow();
        var expected = new[]
        {
            (User: "employee-a", JobFunction: "Pta", Role: "Employee"),
            (User: "employee-b", JobFunction: "Pka", Role: "Employee"),
            (User: "owner", JobFunction: "Pharmacist", Role: "Manager"),
        };
        foreach (var (member, seeded) in listed.Items.Zip(expected))
        {
            member.PersonId.ShouldBe(await database.SubjectOfAsync(seeded.User));
            member.Name.ShouldBe(await database.NameOfAsync(seeded.User));
            member.JobFunction.ShouldBe(seeded.JobFunction);
            member.Role.ShouldBe(seeded.Role);
            (now - member.Since).Duration().ShouldBeLessThan(TimeSpan.FromMinutes(5));
        }
    }

    [Fact]
    public async Task The_members_come_a_page_at_a_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownMembership = await MembershipOfAsync(owner, pharmacy, cancellationToken);
        await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        await database.SeedMemberAsync(pharmacy, "employee-b", "Employee", "Pka", cancellationToken);

        var listed = await owner.GetFromJsonAsync<Paged<Member>>(ListUri(pharmacy, "members", "?page=2&pageSize=2"), cancellationToken);

        listed.ShouldNotBeNull();
        listed.Items.ShouldHaveSingleItem().MembershipId.ShouldBe(ownMembership);
        listed.TotalCount.ShouldBe(3);
        listed.Page.ShouldBe(2);
        listed.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task A_manager_lists_open_and_expired_invitations_by_expiry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        var (latestMail, sooner, expiredMail) = (Invitee(), Invitee(), Invitee());
        var latest = await database.InsertInvitationAsync(pharmacy, latestMail, TokenHash(), "Invited", cancellationToken, now.AddDays(10), "Manager", "Pharmacist");
        var expired = await database.InsertInvitationAsync(pharmacy, expiredMail, TokenHash(), "Invited", cancellationToken, now.AddDays(-1), "Employee", "Pka");
        var soon = await database.InsertInvitationAsync(pharmacy, sooner, TokenHash(), "Invited", cancellationToken, now.AddDays(3), "Employee", "Trainee");
        await database.InsertInvitationAsync(pharmacy, Invitee(), TokenHash(), "Revoked", cancellationToken, now.AddDays(1));
        await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        var listed = await owner.GetFromJsonAsync<Paged<Invitation>>(ListUri(pharmacy, "invitations"), cancellationToken);

        listed.ShouldNotBeNull();
        listed.Items.Select(invitation => invitation.MembershipId).ShouldBe([expired, soon, latest]);
        listed.TotalCount.ShouldBe(3);
        var expected = new[]
        {
            (Email: expiredMail, Role: "Employee", JobFunction: "Pka", ExpiresAt: now.AddDays(-1)),
            (Email: sooner, Role: "Employee", JobFunction: "Trainee", ExpiresAt: now.AddDays(3)),
            (Email: latestMail, Role: "Manager", JobFunction: "Pharmacist", ExpiresAt: now.AddDays(10)),
        };
        foreach (var (invitation, inserted) in listed.Items.Zip(expected))
        {
            invitation.Email.ShouldBe(inserted.Email);
            invitation.Role.ShouldBe(inserted.Role);
            invitation.JobFunction.ShouldBe(inserted.JobFunction);
            // The database keeps microseconds.
            (invitation.ExpiresAt - inserted.ExpiresAt).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task An_ended_membership_appears_in_neither_list()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ben = await database.SeedMemberAsync(pharmacy, "employee-b", "Employee", "Pta", cancellationToken);
        using var ended = await owner.DeleteAsync(new Uri($"/organisation/pharmacies/{pharmacy}/members/{ben}", UriKind.Relative), cancellationToken);
        ended.StatusCode.ShouldBe(HttpStatusCode.OK);

        var members = await owner.GetFromJsonAsync<Paged<Member>>(ListUri(pharmacy, "members"), cancellationToken);
        var invitations = await owner.GetFromJsonAsync<Paged<Invitation>>(ListUri(pharmacy, "invitations"), cancellationToken);

        members.ShouldNotBeNull();
        members.Items.Select(member => member.MembershipId).ShouldNotContain(ben);
        members.TotalCount.ShouldBe(1);
        invitations.ShouldNotBeNull();
        invitations.Items.Select(invitation => invitation.MembershipId).ShouldNotContain(ben);
        invitations.TotalCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("invitations")]
    public async Task A_manager_may_list(string list)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);

        using var listed = await owner.GetAsync(ListUri(pharmacy, list), cancellationToken);

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("members")]
    [InlineData("invitations")]
    public async Task A_member_without_the_capability_to_manage_members_is_forbidden(string list)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var refused = await employee.GetAsync(ListUri(pharmacy, list), cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("invitations")]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_does_not_exist(string list)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var pharmacy = await RegisterAsync(owner, cancellationToken);

        using var refused = await visitor.GetAsync(ListUri(pharmacy, list), cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("invitations")]
    public async Task An_anonymous_caller_is_unauthorized(string list)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var anonymous = aspire.CreateHostClient();
        var pharmacy = await RegisterAsync(owner, cancellationToken);

        using var refused = await anonymous.GetAsync(ListUri(pharmacy, list), cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("invitations")]
    public async Task A_page_size_above_the_maximum_is_refused(string list)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);

        using var refused = await owner.GetAsync(ListUri(pharmacy, list, "?pageSize=101"), cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("members")]
    [InlineData("invitations")]
    public async Task The_empty_pharmacy_id_is_no_pharmacy(string list)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        await RegisterAsync(owner, cancellationToken);

        using var refused = await owner.GetAsync(ListUri(Guid.Empty, list), cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
    }

    private static Uri ListUri(Guid pharmacy, string list, string query = "") =>
        new($"/organisation/pharmacies/{pharmacy}/{list}{query}", UriKind.Relative);

    private static string Invitee() => $"invitee-{Guid.CreateVersion7():N}@example.org";

    private static string TokenHash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> MembershipOfAsync(HttpClient client, Guid pharmacy, CancellationToken cancellationToken)
    {
        var read = await client.GetFromJsonAsync<MyPharmacy>(new Uri($"/organisation/pharmacies/{pharmacy}", UriKind.Relative), cancellationToken);
        return read!.MembershipId;
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        (problem?.Code).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private sealed record Paged<T>(List<T> Items, int TotalCount, int Page, int PageSize);

    private sealed record Member(Guid MembershipId, Guid PersonId, string Name, string JobFunction, string Role, DateTimeOffset Since);

    private sealed record Invitation(Guid MembershipId, string Email, string JobFunction, string Role, DateTimeOffset ExpiresAt);

    private sealed record MyPharmacy(Guid Id, string Name, Guid MembershipId, string Role, string JobFunction, bool CanManageMembers, bool CanAssignCourses);

    private sealed record Problem(string Code);
}
