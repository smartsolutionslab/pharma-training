using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A manager changes an assignment's due date, withdraws it and reads it with its history through the edge,
/// <c>GET|PATCH|DELETE /compliance/pharmacies/{id}/assignments/{assignmentId}</c>, and Organisation's ending of a
/// membership cancels the leaver's active assignments in that pharmacy (#30). Members and courses reach Compliance as
/// their events, delivered and awaited one after the other; assignments are made through
/// <c>POST /compliance/pharmacies/{id}/assignments</c>, and an assignment is moved to in progress or completed by SQL,
/// since no endpoint does that yet. Each test has a pharmacy and courses of its own.
/// </summary>
public class ComplianceAssignmentChangesTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string EndingsById = "select count(*) from compliance.membership_endings where id = @id";
    private const string Withdrawn = "Von der Leitung zurückgezogen";
    private const string MembershipEnded = "Mitgliedschaft beendet";
    private const int ConcurrentChanges = 10;

    private readonly OrganisationDatabase organisation = new(aspire);

    [Fact]
    public async Task A_manager_changes_the_due_date_and_reads_who_changed_what_when_and_why()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);
        var before = TruncatedToMicroseconds(TimeProvider.System.GetUtcNow());

        using var response = await ChangeAsync(manager, setting.Pharmacy, assignment, InTwoMonths(), "Urlaub der Mitarbeiterin", cancellationToken);

        var after = TimeProvider.System.GetUtcNow();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        var read = await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken);
        read.DueDate.ShouldBe(InTwoMonths());
        read.Status.ShouldBe("Open");
        var change = read.History.ShouldHaveSingleItem();
        change.Kind.ShouldBe("DueDateChanged");
        change.ChangedBy.ShouldBe(setting.Manager);
        change.ChangedAt.ShouldBeInRange(before, after);
        change.PreviousDueDate.ShouldBe(InAMonth());
        change.NewDueDate.ShouldBe(InTwoMonths());
        change.Reason.ShouldBe("Urlaub der Mitarbeiterin");
        (await DueDateAsync(assignment, cancellationToken)).ShouldBe(InTwoMonths());
    }

    [Fact]
    public async Task A_blank_reason_is_kept_as_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);

        using var response = await ChangeAsync(manager, setting.Pharmacy, assignment, InTwoMonths(), "   ", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        (await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken)).History.ShouldHaveSingleItem().Reason.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_due_date_again_answers_ok_and_leaves_no_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);

        using var response = await ChangeAsync(manager, setting.Pharmacy, assignment, InAMonth(), "Urlaub", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        var read = await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken);
        read.DueDate.ShouldBe(InAMonth());
        read.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_due_date_or_reason_the_rules_refuse_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);

        using (var past = await ChangeAsync(manager, setting.Pharmacy, assignment, Today().AddDays(-2), null, cancellationToken))
        {
            await ShouldBeRefusedAsync(past, HttpStatusCode.UnprocessableEntity, "compliance.due-date-in-the-past", cancellationToken);
        }

        using (var tooFar = await ChangeAsync(manager, setting.Pharmacy, assignment, Today().AddYears(10).AddDays(2), null, cancellationToken))
        {
            await ShouldBeRefusedAsync(tooFar, HttpStatusCode.BadRequest, "compliance.due-date-too-far", cancellationToken);
        }

        using (var missing = await manager.PatchAsJsonAsync(AssignmentUri(setting.Pharmacy, assignment), new { reason = "Urlaub" }, cancellationToken))
        {
            await ShouldBeRefusedAsync(missing, HttpStatusCode.BadRequest, "compliance.due-date-missing", cancellationToken);
        }

        using (var tooLong = await ChangeAsync(manager, setting.Pharmacy, assignment, InTwoMonths(), new string('x', 501), cancellationToken))
        {
            await ShouldBeRefusedAsync(tooLong, HttpStatusCode.BadRequest, "compliance.invalid-change-reason", cancellationToken);
        }

        (await DueDateAsync(assignment, cancellationToken)).ShouldBe(InAMonth());
        (await HistoryLengthAsync(assignment, cancellationToken)).ShouldBe(0);
        (await StatusAsync(assignment, cancellationToken)).ShouldBe("Open");
    }

    /// <summary>PostgreSQL refuses U+0000 in the jsonb history (22P05); the reason is refused before it gets there.</summary>
    [Fact]
    public async Task A_reason_with_a_control_character_is_refused_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);

        using (var refused = await ChangeAsync(manager, setting.Pharmacy, assignment, InTwoMonths(), "Urlaub\u0000", cancellationToken))
        {
            await ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, "compliance.invalid-change-reason", cancellationToken);
        }

        (await DueDateAsync(assignment, cancellationToken)).ShouldBe(InAMonth());
        (await HistoryLengthAsync(assignment, cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// Nobody manages their own assignment (orchestrator's default on #269, the owner may overrule it): another manager of
    /// the pharmacy must move or withdraw it. The local user owner stands in as the second manager.
    /// </summary>
    [Fact]
    public async Task A_manager_may_not_move_or_withdraw_their_own_assignment_but_another_manager_may()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var colleague = await organisation.SubjectOfAsync("owner");
        await DeliverAsync(StartedType, Started(Guid.CreateVersion7(), colleague, setting.Pharmacy, await organisation.NameOfAsync("owner"), "Pharmacist", "Manager"), cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var own = await AssignAsync(manager, setting.Pharmacy, course, setting.Manager, cancellationToken);

        using (var moved = await ChangeAsync(manager, setting.Pharmacy, own, InTwoMonths(), null, cancellationToken))
        {
            await ShouldBeRefusedAsync(moved, HttpStatusCode.Forbidden, "compliance.own-assignment", cancellationToken);
        }

        using (var withdrawn = await manager.DeleteAsync(AssignmentUri(setting.Pharmacy, own), cancellationToken))
        {
            await ShouldBeRefusedAsync(withdrawn, HttpStatusCode.Forbidden, "compliance.own-assignment", cancellationToken);
        }

        (await HistoryLengthAsync(own, cancellationToken)).ShouldBe(0);

        using var other = await aspire.CreateHostClientAsync("owner");
        using (var movedByColleague = await ChangeAsync(other, setting.Pharmacy, own, InTwoMonths(), null, cancellationToken))
        {
            movedByColleague.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await DueDateAsync(own, cancellationToken)).ShouldBe(InTwoMonths());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Only_a_manager_of_the_pharmacy_reaches_an_assignment_of_it(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var other = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var employee = await aspire.CreateHostClientAsync("employee-a");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        using var anonymous = aspire.CreateHostClient();
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeB, cancellationToken);
        var elsewhere = await AssignAsync(manager, other.Pharmacy, course, other.EmployeeB, cancellationToken);

        using (var response = await SendAsync(employee, method, setting.Pharmacy, assignment, cancellationToken))
        {
            await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "compliance.capability-missing", cancellationToken);
        }

        using (var response = await SendAsync(visitor, method, setting.Pharmacy, assignment, cancellationToken))
        {
            await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
        }

        using (var response = await SendAsync(anonymous, method, setting.Pharmacy, assignment, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        using (var response = await SendAsync(manager, method, setting.Pharmacy, Guid.CreateVersion7(), cancellationToken))
        {
            await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.assignment-not-found", cancellationToken);
        }

        using (var response = await SendAsync(manager, method, Guid.Empty, assignment, cancellationToken))
        {
            await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
        }

        using (var response = await SendAsync(manager, method, setting.Pharmacy, Guid.Empty, cancellationToken))
        {
            await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.assignment-not-found", cancellationToken);
        }

        using (var response = await SendAsync(manager, method, setting.Pharmacy, elsewhere, cancellationToken))
        {
            await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.assignment-not-found", cancellationToken);
        }

        List<Guid> untouchedOnes = [assignment, elsewhere];
        foreach (var untouched in untouchedOnes)
        {
            (await StatusAsync(untouched, cancellationToken)).ShouldBe("Open");
            (await DueDateAsync(untouched, cancellationToken)).ShouldBe(InAMonth());
            (await HistoryLengthAsync(untouched, cancellationToken)).ShouldBe(0);
        }
    }

    [Fact]
    public async Task A_manager_withdraws_an_assignment_once_and_the_course_can_be_assigned_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);

        using (var withdrawn = await manager.DeleteAsync(AssignmentUri(setting.Pharmacy, assignment), cancellationToken))
        {
            withdrawn.StatusCode.ShouldBe(HttpStatusCode.OK, await withdrawn.Content.ReadAsStringAsync(cancellationToken));
        }

        var read = await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken);
        read.Status.ShouldBe("Cancelled");
        var change = read.History.ShouldHaveSingleItem();
        change.Kind.ShouldBe("Cancelled");
        change.ChangedBy.ShouldBe(setting.Manager);
        change.PreviousDueDate.ShouldBeNull();
        change.NewDueDate.ShouldBeNull();
        change.Reason.ShouldBe(Withdrawn);

        using (var again = await manager.DeleteAsync(AssignmentUri(setting.Pharmacy, assignment), cancellationToken))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync(cancellationToken));
        }

        (await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken)).History.ShouldHaveSingleItem();

        using (var changed = await ChangeAsync(manager, setting.Pharmacy, assignment, InTwoMonths(), null, cancellationToken))
        {
            await ShouldBeRefusedAsync(changed, HttpStatusCode.Conflict, "compliance.assignment-not-active", cancellationToken);
        }

        var reassigned = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);
        reassigned.ShouldNotBe(assignment);
        (await StatusAsync(reassigned, cancellationToken)).ShouldBe("Open");
    }

    [Fact]
    public async Task A_completed_assignment_cannot_be_withdrawn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);
        await CompleteAsync(assignment, cancellationToken);

        using var response = await manager.DeleteAsync(AssignmentUri(setting.Pharmacy, assignment), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Conflict, "compliance.assignment-not-active", cancellationToken);
        (await StatusAsync(assignment, cancellationToken)).ShouldBe("Completed");
        (await HistoryLengthAsync(assignment, cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// The API exposes no version, so a change cannot be stale on the client's side; what races is two saves of the same
    /// row, and the one that lost is told so instead of overwriting the other (docs/adr/0002).
    /// </summary>
    [Fact]
    public async Task Of_ten_changes_of_one_due_date_at_once_each_saved_one_leaves_its_entry_and_the_others_are_told_the_assignments_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);
        List<DateOnly> dates = [.. Enumerable.Range(1, ConcurrentChanges).Select(day => InTwoMonths().AddDays(day))];

        var responses = await Task.WhenAll(dates.Select(date => ChangeAsync(manager, setting.Pharmacy, assignment, date, $"Änderung {date:O}", cancellationToken)));
        try
        {
            List<string> answers = [];
            List<DateOnly> saved = [];
            for (var index = 0; index < responses.Length; index++)
            {
                var body = await responses[index].Content.ReadAsStringAsync(cancellationToken);
                answers.Add($"{dates[index]:O}: {(int)responses[index].StatusCode} {body}");
                if (responses[index].StatusCode == HttpStatusCode.OK)
                {
                    saved.Add(dates[index]);
                }
            }

            var described = string.Join(" | ", answers);
            for (var index = 0; index < responses.Length; index++)
            {
                if (responses[index].StatusCode != HttpStatusCode.OK)
                {
                    responses[index].StatusCode.ShouldBe(HttpStatusCode.Conflict, described);
                    var problem = JsonSerializer.Deserialize<Problem>(await responses[index].Content.ReadAsStringAsync(cancellationToken), JsonSerializerOptions.Web);
                    (problem?.Code).ShouldBe("compliance.assignments-changed", described);
                }
            }

            saved.ShouldNotBeEmpty(described);
            var read = await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken);
            read.History.Count.ShouldBe(saved.Count, described);
            read.History.Select(change => change.NewDueDate.GetValueOrDefault()).ShouldBe(saved, ignoreOrder: true, described);
            read.History.ShouldAllBe(change => change.Kind == "DueDateChanged");
            saved.ShouldContain(read.DueDate, described);
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
    public async Task Leaving_cancels_the_leavers_open_and_in_progress_assignments_in_that_pharmacy_and_nothing_else()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var other = await PharmacyAsync(cancellationToken);
        var first = await CourseAsync(cancellationToken);
        var second = await CourseAsync(cancellationToken);
        var third = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var open = await AssignAsync(manager, setting.Pharmacy, first, setting.EmployeeA, cancellationToken);
        var inProgress = await AssignAsync(manager, setting.Pharmacy, second, setting.EmployeeA, cancellationToken);
        var completed = await AssignAsync(manager, setting.Pharmacy, third, setting.EmployeeA, cancellationToken);
        var elsewhere = await AssignAsync(manager, other.Pharmacy, first, other.EmployeeA, cancellationToken);
        var colleagues = await AssignAsync(manager, setting.Pharmacy, first, setting.EmployeeB, cancellationToken);
        await StartAsync(inProgress, cancellationToken);
        await CompleteAsync(completed, cancellationToken);

        await DeliverAsync(EndedType, Ended(setting.EmployeeAMembership, setting.EmployeeA, setting.Pharmacy), cancellationToken);

        List<Guid> active = [open, inProgress];
        foreach (var cancelled in active)
        {
            var read = await ReadAsync(manager, setting.Pharmacy, cancelled, cancellationToken);
            read.Status.ShouldBe("Cancelled");
            var change = read.History.ShouldHaveSingleItem();
            change.Kind.ShouldBe("Cancelled");
            change.ChangedBy.ShouldBe(setting.EmployeeA);
            change.ChangedAt.ShouldBeGreaterThanOrEqualTo(read.AssignedAt);
            change.Reason.ShouldBe(MembershipEnded);
        }

        var stillCompleted = await ReadAsync(manager, setting.Pharmacy, completed, cancellationToken);
        stillCompleted.Status.ShouldBe("Completed");
        stillCompleted.History.ShouldBeEmpty();
        (await StatusAsync(elsewhere, cancellationToken)).ShouldBe("Open");
        (await HistoryLengthAsync(elsewhere, cancellationToken)).ShouldBe(0);
        (await StatusAsync(colleagues, cancellationToken)).ShouldBe("Open");
        (await HistoryLengthAsync(colleagues, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task An_ending_delivered_again_cancels_nothing_twice_and_stores_one_ending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var first = await CourseAsync(cancellationToken);
        var second = await CourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var open = await AssignAsync(manager, setting.Pharmacy, first, setting.EmployeeA, cancellationToken);
        var inProgress = await AssignAsync(manager, setting.Pharmacy, second, setting.EmployeeA, cancellationToken);
        await StartAsync(inProgress, cancellationToken);
        var ended = Ended(setting.EmployeeAMembership, setting.EmployeeA, setting.Pharmacy);
        await DeliverAsync(EndedType, ended, cancellationToken);

        // The same event, metadata and all, under another envelope id, so the inbox lets it through to the consumer.
        await DeliverAsync(EndedType, ended, cancellationToken);

        List<Guid> active = [open, inProgress];
        foreach (var cancelled in active)
        {
            var read = await ReadAsync(manager, setting.Pharmacy, cancelled, cancellationToken);
            read.Status.ShouldBe("Cancelled");
            read.History.ShouldHaveSingleItem().Reason.ShouldBe(MembershipEnded);
        }

        (await CountAsync(EndingsById, setting.EmployeeAMembership, cancellationToken)).ShouldBe(1);
    }

    /// <summary>A re-invited person has a new membership; while one of theirs there has not ended, their assignments stay.</summary>
    [Fact]
    public async Task Ending_one_of_two_memberships_in_the_pharmacy_cancels_nothing_until_the_other_ends_too()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync(cancellationToken);
        var rejoined = Guid.CreateVersion7();
        await DeliverAsync(StartedType, Started(rejoined, setting.EmployeeA, setting.Pharmacy, await organisation.NameOfAsync("employee-a"), "Pta", "Employee"), cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, course, setting.EmployeeA, cancellationToken);

        await DeliverAsync(EndedType, Ended(setting.EmployeeAMembership, setting.EmployeeA, setting.Pharmacy), cancellationToken);

        (await StatusAsync(assignment, cancellationToken)).ShouldBe("Open");
        (await HistoryLengthAsync(assignment, cancellationToken)).ShouldBe(0);

        await DeliverAsync(EndedType, Ended(rejoined, setting.EmployeeA, setting.Pharmacy), cancellationToken);

        var read = await ReadAsync(manager, setting.Pharmacy, assignment, cancellationToken);
        read.Status.ShouldBe("Cancelled");
        read.History.ShouldHaveSingleItem().Reason.ShouldBe(MembershipEnded);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

    private static DateOnly InAMonth() => Today().AddDays(30);

    private static DateOnly InTwoMonths() => Today().AddDays(60);

    /// <summary>PostgreSQL keeps microseconds; an instant read before the call is compared at that precision.</summary>
    private static DateTimeOffset TruncatedToMicroseconds(DateTimeOffset instant) => instant.AddTicks(-(instant.Ticks % 10));

    private static Uri AssignmentsUri(Guid pharmacy) => new($"/compliance/pharmacies/{pharmacy}/assignments", UriKind.Relative);

    private static Uri AssignmentUri(Guid pharmacy, Guid assignment) => new($"/compliance/pharmacies/{pharmacy}/assignments/{assignment}", UriKind.Relative);

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, Guid pharmacy, Guid assignment, DateOnly dueDate, string? reason, CancellationToken cancellationToken) =>
        client.PatchAsJsonAsync(AssignmentUri(pharmacy, assignment), new { dueDate, reason }, cancellationToken);

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, Guid pharmacy, Guid assignment, CancellationToken cancellationToken) => method switch
    {
        "GET" => client.GetAsync(AssignmentUri(pharmacy, assignment), cancellationToken),
        "PATCH" => ChangeAsync(client, pharmacy, assignment, InTwoMonths(), "Urlaub", cancellationToken),
        "DELETE" => client.DeleteAsync(AssignmentUri(pharmacy, assignment), cancellationToken),
        _ => throw new InvalidOperationException($"No request is sent with {method}."),
    };

    /// <summary>Assigns the course to the learner through the endpoint, due in a month, and answers the assignment's id.</summary>
    private static async Task<Guid> AssignAsync(HttpClient client, Guid pharmacy, Guid course, Guid learner, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(AssignmentsUri(pharmacy), new { courseId = course, personIds = new[] { learner }, dueDate = InAMonth() }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var answer = JsonSerializer.Deserialize<AssignmentAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
        return answer.Assigned.ShouldHaveSingleItem(body).AssignmentId;
    }

    private static async Task<AssignmentView> ReadAsync(HttpClient client, Guid pharmacy, Guid assignment, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(AssignmentUri(pharmacy, assignment), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var read = JsonSerializer.Deserialize<AssignmentView>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
        read.Id.ShouldBe(assignment);
        read.PharmacyId.ShouldBe(pharmacy);
        return read;
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        (problem?.Code).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private static StartedEvent Started(Guid membershipId, Guid personId, Guid pharmacyId, string displayName, string jobFunction, string role) =>
        new(membershipId, personId, pharmacyId, displayName, jobFunction, role, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EndedEvent Ended(Guid membershipId, Guid personId, Guid pharmacyId) =>
        new(membershipId, personId, pharmacyId, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static PublishedEvent Published(Guid courseId) =>
        new(
            courseId,
            Guid.CreateVersion7(),
            1,
            $"Hygiene {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>
    /// A pharmacy of its own in Compliance: developer as its manager, employee-a and employee-b as its employees, each
    /// with a membership id of its own.
    /// </summary>
    private async Task<Setting> PharmacyAsync(CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        var manager = await organisation.SubjectOfAsync("developer");
        var employeeA = await organisation.SubjectOfAsync("employee-a");
        var employeeB = await organisation.SubjectOfAsync("employee-b");
        var setting = new Setting(pharmacy, manager, employeeA, employeeB, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await DeliverAsync(StartedType, Started(setting.ManagerMembership, manager, pharmacy, await organisation.NameOfAsync("developer"), "Pharmacist", "Manager"), cancellationToken);
        await DeliverAsync(StartedType, Started(setting.EmployeeAMembership, employeeA, pharmacy, await organisation.NameOfAsync("employee-a"), "Pta", "Employee"), cancellationToken);
        await DeliverAsync(StartedType, Started(setting.EmployeeBMembership, employeeB, pharmacy, await organisation.NameOfAsync("employee-b"), "Pka", "Employee"), cancellationToken);
        return setting;
    }

    /// <summary>A course of its own at version 1.</summary>
    private async Task<Guid> CourseAsync(CancellationToken cancellationToken)
    {
        var course = Published(Guid.CreateVersion7());
        await DeliverAsync(PublishedType, course, cancellationToken);
        return course.CourseId;
    }

    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private Task<long> CountAsync(string query, Guid id, CancellationToken cancellationToken) =>
        ComplianceDatabase.CountAsync(aspire, query, id, cancellationToken);

    /// <summary>The learner opened the course; no endpoint does this yet.</summary>
    private Task StartAsync(Guid assignment, CancellationToken cancellationToken) =>
        ExecuteAsync("update compliance.assignments set status = 'InProgress', started_at = now() where id = @id", assignment, null, cancellationToken);

    /// <summary>A certificate was issued for the course; no consumer does this yet.</summary>
    private Task CompleteAsync(Guid assignment, CancellationToken cancellationToken) =>
        ExecuteAsync("update compliance.assignments set status = 'Completed', completed_at = now(), certificate_id = @certificate where id = @id", assignment, Guid.CreateVersion7(), cancellationToken);

    private async Task ExecuteAsync(string statement, Guid assignment, Guid? certificate, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(statement, connection);
        command.Parameters.AddWithValue("id", assignment);
        if (certificate is { } issued)
        {
            command.Parameters.AddWithValue("certificate", issued);
        }

        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }

    private Task<string> StatusAsync(Guid assignment, CancellationToken cancellationToken) =>
        ScalarAsync<string>("select status from compliance.assignments where id = @id", assignment, cancellationToken);

    private Task<DateOnly> DueDateAsync(Guid assignment, CancellationToken cancellationToken) =>
        ScalarAsync<DateOnly>("select due_date from compliance.assignments where id = @id", assignment, cancellationToken);

    private Task<int> HistoryLengthAsync(Guid assignment, CancellationToken cancellationToken) =>
        ScalarAsync<int>("select jsonb_array_length(history) from compliance.assignments where id = @id", assignment, cancellationToken);

    private async Task<T> ScalarAsync<T>(string query, Guid assignment, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", assignment);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"Compliance holds no assignment {assignment}.");
        return reader.GetFieldValue<T>(0);
    }

    private sealed record Setting(Guid Pharmacy, Guid Manager, Guid EmployeeA, Guid EmployeeB, Guid ManagerMembership, Guid EmployeeAMembership, Guid EmployeeBMembership);

    private sealed record AssignmentAnswer(IReadOnlyList<AssignedLearner> Assigned, IReadOnlyList<RefusedLearner> Refused);

    private sealed record AssignedLearner(Guid PersonId, Guid AssignmentId);

    private sealed record RefusedLearner(Guid PersonId, string Code, string Message);

    private sealed record AssignmentView(
        Guid Id,
        Guid PharmacyId,
        Guid LearnerId,
        Guid CourseId,
        DateOnly DueDate,
        int? RecurrenceMonths,
        Guid AssignedBy,
        DateTimeOffset AssignedAt,
        string Status,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        IReadOnlyList<ChangeView> History);

    private sealed record ChangeView(string Kind, Guid ChangedBy, DateTimeOffset ChangedAt, DateOnly? PreviousDueDate, DateOnly? NewDueDate, string? Reason);

    private sealed record Problem(string Code);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
