using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The team matrix through the edge, <c>GET /compliance/pharmacies/{id}/matrix</c> (#34): the pharmacy's active members
/// by the courses assigned there, each cell the state its latest non-cancelled assignment shows today in German time,
/// and the compliance rate, the share of active assignments (open, in progress, locked, overdue) that are not overdue,
/// over every filtered row, in total and per course; only for a caller whose current role there grants viewing the
/// team's compliance (docs/adr/0003). Each test has a pharmacy of its own, where developer is the manager "Mara Manager",
/// employee-a the Pta "Anna Arndt" and employee-b the Pka "Berta Braun"; assignments are made through the edge and moved
/// on in Compliance's table, since the events that complete them belong to other issues.
/// </summary>
public class ComplianceMatrixTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";

    private static readonly Lazy<TimeZoneInfo> GermanTime = new(() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

    private readonly OrganisationDatabase organisation = new(aspire);

    [Fact]
    public async Task A_manager_sees_members_by_courses_with_their_states_and_the_rate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await ScenarioAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await manager.GetAsync(MatrixUri(scenario.Setting.Pharmacy), cancellationToken);

        var matrix = await MatrixOfAsync(response, cancellationToken);
        matrix.Courses.ShouldBe(
        [
            new MatrixCourse(scenario.FirstAid.Id, scenario.FirstAid.Title, 1, 0, 1.0),
            new MatrixCourse(scenario.Hygiene.Id, scenario.Hygiene.Title, 2, 1, 0.5),
        ]);
        matrix.Members.Items.Select(row => (row.PersonId, row.Name, row.JobFunction)).ShouldBe(
        [
            (scenario.Setting.EmployeeA.PersonId, "Anna Arndt", "Pta"),
            (scenario.Setting.EmployeeB.PersonId, "Berta Braun", "Pka"),
            (scenario.Setting.Manager.PersonId, "Mara Manager", "Pharmacist"),
        ]);
        matrix.Members.Items[0].Cells.ShouldBe(
        [
            new MatrixCell(scenario.FirstAid.Id, scenario.AnnaFirstAid, "CompletedLate", scenario.Yesterday, scenario.CompletedOn),
            new MatrixCell(scenario.Hygiene.Id, scenario.AnnaHygiene, "InProgress", scenario.InAMonth, null),
        ]);
        matrix.Members.Items[1].Cells.ShouldBe(
        [
            new MatrixCell(scenario.FirstAid.Id, scenario.BertaFirstAid, "Open", scenario.InAMonth, null),
            new MatrixCell(scenario.Hygiene.Id, scenario.BertaHygiene, "Overdue", scenario.Yesterday, null),
        ]);
        matrix.Members.Items[2].Cells.ShouldBe(
        [
            new MatrixCell(scenario.Hygiene.Id, scenario.MaraHygiene, "Completed", scenario.InAMonth, scenario.CompletedOn),
        ]);
        matrix.Members.TotalCount.ShouldBe(3);
        matrix.ActiveAssignments.ShouldBe(3);
        matrix.OverdueAssignments.ShouldBe(1);
        matrix.ComplianceRate.ShouldBe(2d / 3d);
    }

    [Fact]
    public async Task Overdue_starts_the_day_after_the_due_date_in_german_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = await CourseAsync("Hygiene", cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var today = DayInGermany(TimeProvider.System.GetUtcNow());
        var dueToday = await AssignAsync(manager, setting.Pharmacy, course.Id, setting.EmployeeA.PersonId, today, cancellationToken);
        var dueYesterday = await AssignAsync(manager, setting.Pharmacy, course.Id, setting.EmployeeB.PersonId, today.AddDays(30), cancellationToken);
        await SetAsync(dueYesterday, "due_date = @due", cancellationToken, ("due", today.AddDays(-1)));

        using var response = await manager.GetAsync(MatrixUri(setting.Pharmacy), cancellationToken);

        var matrix = await MatrixOfAsync(response, cancellationToken);
        RowOf(matrix, setting.EmployeeA.PersonId).Cells.ShouldHaveSingleItem().ShouldBe(new MatrixCell(course.Id, dueToday, "Open", today, null));
        RowOf(matrix, setting.EmployeeB.PersonId).Cells.ShouldHaveSingleItem().ShouldBe(new MatrixCell(course.Id, dueYesterday, "Overdue", today.AddDays(-1), null));
    }

    [Fact]
    public async Task Only_courses_assigned_in_this_pharmacy_appear_and_one_only_cancelled_here_stays_a_column_without_cells()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var otherPharmacy = Guid.CreateVersion7();
        await DeliverAsync(StartedType, Started(setting.Manager.PersonId, otherPharmacy, "Mara Manager", "Pharmacist", "Manager"), cancellationToken);
        var elsewhere = await CourseAsync("Andernorts", cancellationToken);
        var cancelledHere = await CourseAsync("Storniert", cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var inAMonth = DayInGermany(TimeProvider.System.GetUtcNow()).AddDays(30);
        await AssignAsync(manager, otherPharmacy, elsewhere.Id, setting.Manager.PersonId, inAMonth, cancellationToken);
        var cancelled = await AssignAsync(manager, setting.Pharmacy, cancelledHere.Id, setting.EmployeeA.PersonId, inAMonth, cancellationToken);
        await SetAsync(cancelled, "status = 'Cancelled'", cancellationToken);

        using var response = await manager.GetAsync(MatrixUri(setting.Pharmacy), cancellationToken);

        var matrix = await MatrixOfAsync(response, cancellationToken);
        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new MatrixCourse(cancelledHere.Id, cancelledHere.Title, 0, 0, null));
        matrix.Members.Items.Count.ShouldBe(3);
        matrix.Members.Items.ShouldAllBe(row => row.Cells.Count == 0);
        matrix.ActiveAssignments.ShouldBe(0);
        matrix.OverdueAssignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBeNull();
    }

    [Fact]
    public async Task An_employee_of_the_pharmacy_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var response = await employee.GetAsync(MatrixUri(setting.Pharmacy), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "compliance.capability-missing", cancellationToken);
    }

    [Fact]
    public async Task A_signed_in_non_member_is_told_the_pharmacy_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var response = await visitor.GetAsync(MatrixUri(setting.Pharmacy), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = aspire.CreateHostClient();

        using var response = await anonymous.GetAsync(MatrixUri(Guid.CreateVersion7()), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task The_rows_are_paged_and_the_columns_and_rate_cover_the_whole_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await ScenarioAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var wholeResponse = await manager.GetAsync(MatrixUri(scenario.Setting.Pharmacy), cancellationToken);
        using var pagedResponse = await manager.GetAsync(MatrixUri(scenario.Setting.Pharmacy, "?page=2&pageSize=2"), cancellationToken);

        var whole = await MatrixOfAsync(wholeResponse, cancellationToken);
        var paged = await MatrixOfAsync(pagedResponse, cancellationToken);
        paged.Members.Items.ShouldHaveSingleItem().PersonId.ShouldBe(scenario.Setting.Manager.PersonId);
        paged.Members.TotalCount.ShouldBe(3);
        paged.Members.Page.ShouldBe(2);
        paged.Members.PageSize.ShouldBe(2);
        paged.Courses.ShouldBe(whole.Courses);
        (paged.ActiveAssignments, paged.OverdueAssignments, paged.ComplianceRate).ShouldBe((whole.ActiveAssignments, whole.OverdueAssignments, whole.ComplianceRate));
        (paged.ActiveAssignments, paged.OverdueAssignments).ShouldBe((3, 1));
    }

    [Fact]
    public async Task The_job_function_filter_narrows_the_rows_and_the_rate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await ScenarioAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await manager.GetAsync(MatrixUri(scenario.Setting.Pharmacy, "?jobFunction=Pta"), cancellationToken);

        var matrix = await MatrixOfAsync(response, cancellationToken);
        matrix.Members.Items.ShouldHaveSingleItem().PersonId.ShouldBe(scenario.Setting.EmployeeA.PersonId);
        matrix.Members.TotalCount.ShouldBe(1);
        matrix.Courses.ShouldBe(
        [
            new MatrixCourse(scenario.FirstAid.Id, scenario.FirstAid.Title, 0, 0, null),
            new MatrixCourse(scenario.Hygiene.Id, scenario.Hygiene.Title, 1, 0, 1.0),
        ]);
        matrix.ActiveAssignments.ShouldBe(1);
        matrix.OverdueAssignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBe(1.0);
    }

    [Fact]
    public async Task A_member_whose_membership_ended_drops_out_with_their_assignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await ScenarioAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var before = await manager.GetAsync(MatrixUri(scenario.Setting.Pharmacy), cancellationToken);
        (await MatrixOfAsync(before, cancellationToken)).ActiveAssignments.ShouldBe(3);

        await DeliverAsync(EndedType, Ended(scenario.Setting.EmployeeB), cancellationToken);
        using var after = await manager.GetAsync(MatrixUri(scenario.Setting.Pharmacy), cancellationToken);

        var matrix = await MatrixOfAsync(after, cancellationToken);
        matrix.Members.Items.Select(row => row.PersonId).ShouldBe([scenario.Setting.EmployeeA.PersonId, scenario.Setting.Manager.PersonId]);
        matrix.Members.TotalCount.ShouldBe(2);
        matrix.Courses.ShouldBe(
        [
            new MatrixCourse(scenario.FirstAid.Id, scenario.FirstAid.Title, 0, 0, null),
            new MatrixCourse(scenario.Hygiene.Id, scenario.Hygiene.Title, 1, 0, 1.0),
        ]);
        matrix.ActiveAssignments.ShouldBe(1);
        matrix.OverdueAssignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBe(1.0);
    }

    private static Uri MatrixUri(Guid pharmacy, string query = "") => new($"/compliance/pharmacies/{pharmacy}/matrix{query}", UriKind.Relative);

    private static Uri AssignmentsUri(Guid pharmacy) => new($"/compliance/pharmacies/{pharmacy}/assignments", UriKind.Relative);

    /// <summary>The German calendar day of the instant, as Compliance reads due dates and completions.</summary>
    private static DateOnly DayInGermany(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, GermanTime.Value).DateTime);

    private static MatrixRow RowOf(MatrixAnswer matrix, Guid person) => matrix.Members.Items.Single(row => row.PersonId == person);

    private static async Task<MatrixAnswer> MatrixOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<MatrixAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        (problem?.Code).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    /// <summary>Assigns the course to the person through the edge and answers the assignment's id.</summary>
    private static async Task<Guid> AssignAsync(HttpClient manager, Guid pharmacy, Guid course, Guid person, DateOnly dueDate, CancellationToken cancellationToken)
    {
        using var response = await manager.PostAsJsonAsync(AssignmentsUri(pharmacy), new { courseId = course, personIds = new[] { person }, dueDate }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var answer = JsonSerializer.Deserialize<AssignmentAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
        answer.Refused.ShouldBeEmpty(body);
        return answer.Assigned.ShouldHaveSingleItem().AssignmentId;
    }

    private static StartedEvent Started(Guid personId, Guid pharmacyId, string displayName, string jobFunction, string role) =>
        new(Guid.CreateVersion7(), personId, pharmacyId, displayName, jobFunction, role, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EndedEvent Ended(StartedEvent started) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>A pharmacy of its own in Compliance: developer as its manager, employee-a and employee-b as its employees.</summary>
    private async Task<Setting> PharmacyAsync(CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        var manager = Started(await organisation.SubjectOfAsync("developer"), pharmacy, "Mara Manager", "Pharmacist", "Manager");
        var employeeA = Started(await organisation.SubjectOfAsync("employee-a"), pharmacy, "Anna Arndt", "Pta", "Employee");
        var employeeB = Started(await organisation.SubjectOfAsync("employee-b"), pharmacy, "Berta Braun", "Pka", "Employee");
        await DeliverAsync(StartedType, manager, cancellationToken);
        await DeliverAsync(StartedType, employeeA, cancellationToken);
        await DeliverAsync(StartedType, employeeB, cancellationToken);
        return new Setting(pharmacy, manager, employeeA, employeeB);
    }

    /// <summary>A course of its own at version 1, titled with the name and a Guid so no other test's course sorts between.</summary>
    private async Task<Course> CourseAsync(string name, CancellationToken cancellationToken)
    {
        var published = new PublishedEvent(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            $"{name} {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());
        await DeliverAsync(PublishedType, published, cancellationToken);
        return new Course(published.CourseId, published.Title);
    }

    /// <summary>
    /// The pharmacy of <see cref="PharmacyAsync"/> with two courses assigned and a third never assigned. Hygiene: Anna in
    /// progress, Berta overdue, Mara completed. Erste Hilfe: Anna completed late, Berta open. Every day is taken from one
    /// instant, the completion's, so a midnight in between cannot split them.
    /// </summary>
    private async Task<Scenario> ScenarioAsync(CancellationToken cancellationToken)
    {
        var setting = await PharmacyAsync(cancellationToken);
        var hygiene = await CourseAsync("Hygiene", cancellationToken);
        var firstAid = await CourseAsync("Erste Hilfe", cancellationToken);
        await CourseAsync("Nie zugewiesen", cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var completedAt = TimeProvider.System.GetUtcNow();
        var today = DayInGermany(completedAt);
        var inAMonth = today.AddDays(30);
        var yesterday = today.AddDays(-1);

        var annaHygiene = await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeA.PersonId, inAMonth, cancellationToken);
        await SetAsync(annaHygiene, "status = 'InProgress', started_at = @at", cancellationToken, ("at", completedAt));
        var bertaHygiene = await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeB.PersonId, inAMonth, cancellationToken);
        await SetAsync(bertaHygiene, "due_date = @due", cancellationToken, ("due", yesterday));
        var maraHygiene = await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.Manager.PersonId, inAMonth, cancellationToken);
        await SetAsync(maraHygiene, "status = 'Completed', completed_at = @at, certificate_id = @certificate", cancellationToken, ("at", completedAt), ("certificate", Guid.CreateVersion7()));
        var annaFirstAid = await AssignAsync(manager, setting.Pharmacy, firstAid.Id, setting.EmployeeA.PersonId, inAMonth, cancellationToken);
        await SetAsync(annaFirstAid, "due_date = @due, status = 'Completed', completed_at = @at, certificate_id = @certificate", cancellationToken, ("due", yesterday), ("at", completedAt), ("certificate", Guid.CreateVersion7()));
        var bertaFirstAid = await AssignAsync(manager, setting.Pharmacy, firstAid.Id, setting.EmployeeB.PersonId, inAMonth, cancellationToken);

        return new Scenario(setting, hygiene, firstAid, annaHygiene, bertaHygiene, maraHygiene, annaFirstAid, bertaFirstAid, inAMonth, yesterday, today);
    }

    /// <summary>Moves the assignment on in Compliance's table, as the events of other issues will.</summary>
    private async Task SetAsync(Guid assignment, string set, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand($"update compliance.assignments set {set} where id = @id", connection);
        command.Parameters.AddWithValue("id", assignment);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }

    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private sealed record Setting(Guid Pharmacy, StartedEvent Manager, StartedEvent EmployeeA, StartedEvent EmployeeB);

    private sealed record Course(Guid Id, string Title);

    private sealed record Scenario(
        Setting Setting,
        Course Hygiene,
        Course FirstAid,
        Guid AnnaHygiene,
        Guid BertaHygiene,
        Guid MaraHygiene,
        Guid AnnaFirstAid,
        Guid BertaFirstAid,
        DateOnly InAMonth,
        DateOnly Yesterday,
        DateOnly CompletedOn);

    private sealed record MatrixAnswer(IReadOnlyList<MatrixCourse> Courses, MatrixPage Members, int ActiveAssignments, int OverdueAssignments, double? ComplianceRate);

    private sealed record MatrixCourse(Guid CourseId, string Title, int ActiveAssignments, int OverdueAssignments, double? ComplianceRate);

    private sealed record MatrixPage(IReadOnlyList<MatrixRow> Items, int TotalCount, int Page, int PageSize);

    private sealed record MatrixRow(Guid PersonId, string Name, string JobFunction, IReadOnlyList<MatrixCell> Cells);

    private sealed record MatrixCell(Guid CourseId, Guid AssignmentId, string State, DateOnly DueDate, DateOnly? LastCompletedOn);

    private sealed record AssignmentAnswer(IReadOnlyList<AssignedLearner> Assigned, IReadOnlyList<RefusedLearner> Refused);

    private sealed record AssignedLearner(Guid PersonId, Guid AssignmentId);

    private sealed record RefusedLearner(Guid PersonId, string Code, string Message);

    private sealed record Problem(string Code);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
