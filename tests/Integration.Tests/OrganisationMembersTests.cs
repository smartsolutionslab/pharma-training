using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A pharmacy's management ends a membership or changes its job function and role through the running application:
/// only a manager may, the last manager is never removed, and an ended membership is announced through the broker.
/// The second member is seeded in the database until invitations can be accepted (#10).
/// </summary>
public class OrganisationMembersTests(AspireFixture aspire)
{
    private const string MembershipEndedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";

    private const string MembershipChangedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task A_manager_ends_an_employees_membership_and_the_employee_no_longer_sees_the_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        using var before = await employee.GetAsync(PharmacyUri(pharmacy), cancellationToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var ended = await owner.DeleteAsync(MemberUri(pharmacy, membership), cancellationToken);

        ended.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await database.MembershipAsync(membership, cancellationToken);
        stored.Status.ShouldBe("Ended");
        stored.EndedAt.ShouldNotBeNull();
        using var after = await employee.GetAsync(PharmacyUri(pharmacy), cancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var again = await owner.DeleteAsync(MemberUri(pharmacy, membership), cancellationToken);
        await ShouldBeRefusedAsync(again, HttpStatusCode.NotFound, "organisation.no-active-membership", cancellationToken);
    }

    [Fact]
    public async Task The_only_manager_cannot_end_their_own_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var own = await MembershipOfAsync(owner, pharmacy, cancellationToken);

        using var refused = await owner.DeleteAsync(MemberUri(pharmacy, own), cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Conflict, "organisation.last-manager", cancellationToken);
        (await database.MembershipAsync(own, cancellationToken)).Status.ShouldBe("Active");
    }

    [Fact]
    public async Task A_manager_ends_their_own_membership_once_there_is_a_second_manager()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var second = await aspire.CreateHostClientAsync("employee-b");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var own = await MembershipOfAsync(owner, pharmacy, cancellationToken);
        await database.SeedMemberAsync(pharmacy, "employee-b", "Manager", "Pharmacist", cancellationToken);

        using var ended = await owner.DeleteAsync(MemberUri(pharmacy, own), cancellationToken);

        ended.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var ownerReads = await owner.GetAsync(PharmacyUri(pharmacy), cancellationToken);
        ownerReads.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var remaining = await second.GetFromJsonAsync<MyPharmacy>(PharmacyUri(pharmacy), cancellationToken);
        remaining!.Role.ShouldBe("Manager");
    }

    [Fact]
    public async Task An_employee_may_not_end_a_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownerMembership = await MembershipOfAsync(owner, pharmacy, cancellationToken);
        var own = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var others = await employee.DeleteAsync(MemberUri(pharmacy, ownerMembership), cancellationToken);
        using var itself = await employee.DeleteAsync(MemberUri(pharmacy, own), cancellationToken);

        await ShouldBeRefusedAsync(others, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
        await ShouldBeRefusedAsync(itself, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
        (await database.MembershipAsync(ownerMembership, cancellationToken)).Status.ShouldBe("Active");
        (await database.MembershipAsync(own, cancellationToken)).Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownerMembership = await MembershipOfAsync(owner, pharmacy, cancellationToken);

        using var ended = await visitor.DeleteAsync(MemberUri(pharmacy, ownerMembership), cancellationToken);
        using var changed = await visitor.PatchAsJsonAsync(MemberUri(pharmacy, ownerMembership), new { jobFunction = "Pka", role = "Employee" }, cancellationToken);

        await ShouldBeRefusedAsync(ended, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        await ShouldBeRefusedAsync(changed, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_anonymous_caller_neither_ends_nor_changes_a_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var anonymous = aspire.CreateHostClient();
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownerMembership = await MembershipOfAsync(owner, pharmacy, cancellationToken);

        using var ended = await anonymous.DeleteAsync(MemberUri(pharmacy, ownerMembership), cancellationToken);
        using var changed = await anonymous.PatchAsJsonAsync(MemberUri(pharmacy, ownerMembership), new { jobFunction = "Pka", role = "Manager" }, cancellationToken);

        ended.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        changed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_membership_that_is_not_active_in_the_pharmacy_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var otherPharmacy = await RegisterAsync(owner, cancellationToken);
        var elsewhere = await database.SeedMemberAsync(otherPharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        List<Guid> notHere = [Guid.CreateVersion7(), elsewhere, Guid.Empty];

        foreach (var membership in notHere)
        {
            using var ended = await owner.DeleteAsync(MemberUri(pharmacy, membership), cancellationToken);
            using var changed = await owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pka", role = "Employee" }, cancellationToken);

            await ShouldBeRefusedAsync(ended, HttpStatusCode.NotFound, "organisation.no-active-membership", cancellationToken);
            await ShouldBeRefusedAsync(changed, HttpStatusCode.NotFound, "organisation.no-active-membership", cancellationToken);
        }

        (await database.MembershipAsync(elsewhere, cancellationToken)).Status.ShouldBe("Active");
    }

    [Fact]
    public async Task The_empty_pharmacy_id_is_no_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownerMembership = await MembershipOfAsync(owner, pharmacy, cancellationToken);

        using var ended = await owner.DeleteAsync(MemberUri(Guid.Empty, ownerMembership), cancellationToken);
        using var changed = await owner.PatchAsJsonAsync(MemberUri(Guid.Empty, ownerMembership), new { jobFunction = "Pka", role = "Manager" }, cancellationToken);

        await ShouldBeRefusedAsync(ended, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
        await ShouldBeRefusedAsync(changed, HttpStatusCode.NotFound, "organisation.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task Ending_a_membership_announces_it_through_the_broker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);

        // Wolverine declares an event's exchange when it first sends one: end one membership so it exists, then listen.
        var warmUp = await RegisterAsync(owner, cancellationToken);
        using var warmedUp = await owner.DeleteAsync(MemberUri(warmUp, await database.SeedMemberAsync(warmUp, "employee-b", "Employee", "Pta", cancellationToken)), cancellationToken);
        warmedUp.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Broker.WaitForExchangeAsync(connection, MembershipEndedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, MembershipEndedExchange, cancellationToken);
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var ended = await owner.DeleteAsync(MemberUri(pharmacy, membership), cancellationToken);

        ended.StatusCode.ShouldBe(HttpStatusCode.OK);
        var announced = await Broker.ReceiveAsync<MembershipEnded>(channel, queue, received => received.MembershipId == membership, cancellationToken);
        announced.PersonId.ShouldBe(await database.SubjectOfAsync("employee-a"));
        announced.PharmacyId.ShouldBe(pharmacy);
        var stored = await database.MembershipAsync(membership, cancellationToken);
        // The database keeps microseconds; the event carries the instant the domain ended it at.
        (announced.EndedAt - stored.EndedAt!.Value).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Changing_a_membership_announces_it_through_the_broker_with_the_next_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);

        // Wolverine declares an event's exchange when it first sends one: change one membership so it exists, then listen.
        var warmUp = await RegisterAsync(owner, cancellationToken);
        var warmUpMember = await database.SeedMemberAsync(warmUp, "employee-b", "Employee", "Pta", cancellationToken);
        using var warmedUp = await owner.PatchAsJsonAsync(MemberUri(warmUp, warmUpMember), new { jobFunction = "Pka", role = "Employee" }, cancellationToken);
        warmedUp.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Broker.WaitForExchangeAsync(connection, MembershipChangedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, MembershipChangedExchange, cancellationToken);
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var promoted = await owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pta", role = "Manager" }, cancellationToken);

        promoted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await Broker.ReceiveAsync<MembershipChanged>(channel, queue, received => received.MembershipId == membership, cancellationToken);
        second.PersonId.ShouldBe(await database.SubjectOfAsync("employee-a"));
        second.PharmacyId.ShouldBe(pharmacy);
        second.DisplayName.ShouldBe(await database.NameOfAsync("employee-a"));
        second.JobFunction.ShouldBe("Pta");
        second.Role.ShouldBe("Manager");
        second.Version.ShouldBe(2);

        using var demoted = await owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pta", role = "Employee" }, cancellationToken);

        demoted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var third = await Broker.ReceiveAsync<MembershipChanged>(channel, queue, received => received.MembershipId == membership, cancellationToken);
        third.Role.ShouldBe("Employee");
        third.Version.ShouldBe(3);
    }

    [Fact]
    public async Task A_manager_changes_an_employees_job_function_and_role_and_the_employee_reads_them()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var changed = await owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pka", role = "Manager" }, cancellationToken);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = await employee.GetFromJsonAsync<MyPharmacy>(PharmacyUri(pharmacy), cancellationToken);
        read!.MembershipId.ShouldBe(membership);
        read.Role.ShouldBe("Manager");
        read.JobFunction.ShouldBe("Pka");
        read.CanManageMembers.ShouldBeTrue();
    }

    [Fact]
    public async Task The_only_manager_cannot_step_down()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var own = await MembershipOfAsync(owner, pharmacy, cancellationToken);

        using var refused = await owner.PatchAsJsonAsync(MemberUri(pharmacy, own), new { jobFunction = "Pharmacist", role = "Employee" }, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Conflict, "organisation.last-manager", cancellationToken);
        (await database.MembershipAsync(own, cancellationToken)).Role.ShouldBe("Manager");
    }

    [Fact]
    public async Task The_values_a_membership_already_has_are_accepted_and_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var same = await owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pta", role = "Employee" }, cancellationToken);

        same.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await database.MembershipAsync(membership, cancellationToken);
        stored.Role.ShouldBe("Employee");
        stored.JobFunction.ShouldBe("Pta");
    }

    [Theory]
    [InlineData("\"jobFunction\":\"Pka\",\"role\":42", "organisation.invalid-membership-role")]
    [InlineData("\"jobFunction\":\"Pka\",\"role\":\"42\"", "organisation.invalid-membership-role")]
    [InlineData("\"jobFunction\":42,\"role\":\"Manager\"", "organisation.invalid-job-function")]
    [InlineData("\"jobFunction\":\"42\",\"role\":\"Manager\"", "organisation.invalid-job-function")]
    public async Task A_role_or_job_function_that_is_no_known_one_is_refused_as_the_callers_mistake(string fields, string code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        using var body = new StringContent($"{{{fields}}}", Encoding.UTF8, "application/json");

        using var refused = await owner.PatchAsync(MemberUri(pharmacy, membership), body, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, code, cancellationToken);
        (await database.MembershipAsync(membership, cancellationToken)).Role.ShouldBe("Employee");
    }

    [Theory]
    [InlineData("\"jobFunction\":\"Pka\"")]
    [InlineData("\"role\":\"Manager\"")]
    [InlineData("\"jobFunction\":\"Pka\",\"role\":\"Boss\"")]
    public async Task A_missing_or_unknown_field_is_refused_when_the_body_is_read(string fields)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        using var body = new StringContent($"{{{fields}}}", Encoding.UTF8, "application/json");

        using var refused = await owner.PatchAsync(MemberUri(pharmacy, membership), body, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await database.MembershipAsync(membership, cancellationToken)).Role.ShouldBe("Employee");
    }

    [Fact]
    public async Task An_employee_may_not_change_a_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var own = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);

        using var refused = await employee.PatchAsJsonAsync(MemberUri(pharmacy, own), new { jobFunction = "Pta", role = "Manager" }, cancellationToken);

        await ShouldBeRefusedAsync(refused, HttpStatusCode.Forbidden, "organisation.capability-missing", cancellationToken);
        (await database.MembershipAsync(own, cancellationToken)).Role.ShouldBe("Employee");
    }

    [Fact]
    public async Task Changing_only_a_membership_moves_the_pharmacys_row_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        var before = await database.RowVersionAsync(pharmacy, cancellationToken);

        using var changed = await owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pka", role = "Employee" }, cancellationToken);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await database.RowVersionAsync(pharmacy, cancellationToken)).ShouldNotBe(before, "a change to a membership must move its pharmacy's row version");
    }

    [Fact]
    public async Task Of_two_managers_ending_each_others_membership_at_once_one_succeeds_and_the_other_is_told_the_pharmacy_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var second = await aspire.CreateHostClientAsync("employee-b");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var own = await MembershipOfAsync(owner, pharmacy, cancellationToken);
        var other = await database.SeedMemberAsync(pharmacy, "employee-b", "Manager", "Pharmacist", cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var listener = await ListenForEndedMembershipsAsync(owner, connection, channel, cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            var ownerEndsOther = owner.DeleteAsync(MemberUri(pharmacy, other), cancellationToken);
            var otherEndsOwner = second.DeleteAsync(MemberUri(pharmacy, own), cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(ownerEndsOther, otherEndsOwner);
        }

        try
        {
            await ShouldBeOneWonAndOneChangedAsync(responses, cancellationToken);
            (await database.ActiveManagersAsync(pharmacy, cancellationToken)).ShouldBe(1L, "the pharmacy must keep exactly one manager");
            await ShouldAnnounceEachEndedMembershipOnceAsync(owner, channel, listener, [own, other], cancellationToken);
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
    public async Task Of_ending_and_changing_one_membership_at_once_one_succeeds_and_the_other_is_told_the_pharmacy_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.SeedMemberAsync(pharmacy, "employee-a", "Employee", "Pta", cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var listener = await ListenForEndedMembershipsAsync(owner, connection, channel, cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            var end = owner.DeleteAsync(MemberUri(pharmacy, membership), cancellationToken);
            var change = owner.PatchAsJsonAsync(MemberUri(pharmacy, membership), new { jobFunction = "Pka", role = "Manager" }, cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(end, change);
        }

        try
        {
            await ShouldBeOneWonAndOneChangedAsync(responses, cancellationToken);
            var stored = await database.MembershipAsync(membership, cancellationToken);
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                // The end won: the membership is ended with the job function and role it had, the change is not stored.
                (stored.Status, stored.Role, stored.JobFunction).ShouldBe(("Ended", "Employee", "Pta"));
                stored.EndedAt.ShouldNotBeNull();
            }
            else
            {
                stored.ShouldBe(new OrganisationDatabase.StoredMembership("Active", "Manager", "Pka", null), "the change won, so the end must not be stored");
            }

            await ShouldAnnounceEachEndedMembershipOnceAsync(owner, channel, listener, [membership], cancellationToken);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static Uri PharmacyUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}", UriKind.Relative);

    private static Uri MemberUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/members/{membership}", UriKind.Relative);

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> MembershipOfAsync(HttpClient client, Guid pharmacy, CancellationToken cancellationToken)
    {
        var read = await client.GetFromJsonAsync<MyPharmacy>(PharmacyUri(pharmacy), cancellationToken);
        return read!.MembershipId;
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        (problem?.Code).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private static async Task ShouldBeOneWonAndOneChangedAsync(HttpResponseMessage[] responses, CancellationToken cancellationToken)
    {
        List<string> answers = [];
        foreach (var response in responses)
        {
            answers.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        var described = string.Join(" | ", answers);
        responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true, described);
        var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        await ShouldBeRefusedAsync(lost, HttpStatusCode.Conflict, "organisation.pharmacy-changed", cancellationToken);
    }

    /// <summary>
    /// Binds a queue to MembershipEndedV1 before a race. The listener's pharmacy of its own ends one membership first,
    /// so Wolverine has declared the exchange, and later one more, whose announcement marks the end of the race's.
    /// </summary>
    private async Task<EndedMembershipListener> ListenForEndedMembershipsAsync(HttpClient owner, IConnection connection, IChannel channel, CancellationToken cancellationToken)
    {
        var marker = await RegisterAsync(owner, cancellationToken);
        using var warmedUp = await owner.DeleteAsync(MemberUri(marker, await database.SeedMemberAsync(marker, "employee-a", "Employee", "Pta", cancellationToken)), cancellationToken);
        warmedUp.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Broker.WaitForExchangeAsync(connection, MembershipEndedExchange, cancellationToken);
        return new EndedMembershipListener(await Broker.BindQueueAsync(channel, MembershipEndedExchange, cancellationToken), marker);
    }

    /// <summary>
    /// Every membership of the race that is ended in the database was announced exactly once, and one that is not, never:
    /// the loser's announcement must not reach the broker. Organisation sends its announcements of one event type in
    /// the order it commits them, so the marker, ended after the race, comes after any the race sent.
    /// </summary>
    private async Task ShouldAnnounceEachEndedMembershipOnceAsync(HttpClient owner, IChannel channel, EndedMembershipListener listener, IReadOnlyList<Guid> memberships, CancellationToken cancellationToken)
    {
        var marker = await database.SeedMemberAsync(listener.Pharmacy, "employee-b", "Employee", "Pta", cancellationToken);
        using var markerEnded = await owner.DeleteAsync(MemberUri(listener.Pharmacy, marker), cancellationToken);
        markerEnded.StatusCode.ShouldBe(HttpStatusCode.OK);
        var announced = await Broker.ReceiveUntilAsync<MembershipEnded>(channel, listener.Queue, received => received.MembershipId == marker, cancellationToken);

        foreach (var membership in memberships)
        {
            var ended = (await database.MembershipAsync(membership, cancellationToken)).Status == "Ended";
            announced.Count(received => received.MembershipId == membership).ShouldBe(ended ? 1 : 0, $"membership {membership} is {(ended ? "ended" : "not ended")}");
        }
    }

    private sealed record MyPharmacy(Guid Id, string Name, Guid MembershipId, string Role, string JobFunction, bool CanManageMembers, bool CanAssignCourses);

    private sealed record Problem(string Code);

    private sealed record EndedMembershipListener(string Queue, Guid Pharmacy);

    private sealed record MembershipEnded(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt);

    private sealed record MembershipChanged(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, int Version);
}
