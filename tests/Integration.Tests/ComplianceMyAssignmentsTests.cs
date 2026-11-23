using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The learner's dashboard through the edge, <c>GET /compliance/me/assignments?pharmacyId=</c> (#35): the caller's own
/// open and in-progress assignments in the selected pharmacy, overdue first, each with the course version to open, the
/// running session's to continue, else the current one to begin (ADR 0024). Any member of the pharmacy is served; anyone
/// else is told it does not exist (docs/adr/0003). Each test has a pharmacy and courses of its own, where developer is the
/// manager "Mara Manager", employee-a the Pta "Anna Arndt" and employee-b the Pka "Berta Braun"; assignments are made
/// through the edge, and Learning's and Certification's events are delivered to Compliance as their services send them,
/// each with an instant in the past.
/// </summary>
public class ComplianceMyAssignmentsTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string SessionStartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.LearningSessionStartedV1";
    private const string IssuedType = "SmartSolutionsLab.PharmaTraining.Contracts.Certification.CertificateIssuedV1";

    private static readonly Lazy<TimeZoneInfo> GermanTime = new(() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

    private readonly OrganisationDatabase organisation = new(aspire);

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = aspire.CreateHostClient();

        using var response = await anonymous.GetAsync(MyAssignmentsUri($"?pharmacyId={Guid.CreateVersion7()}"), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task A_signed_in_non_member_is_told_the_pharmacy_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var response = await visitor.GetAsync(MyAssignmentsUri($"?pharmacyId={setting.Pharmacy}"), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task A_missing_pharmacy_is_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var response = await employee.GetAsync(MyAssignmentsUri(), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.pharmacy-missing", cancellationToken);
    }

    [Fact]
    public async Task An_empty_pharmacy_id_is_no_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var response = await employee.GetAsync(MyAssignmentsUri($"?pharmacyId={Guid.Empty}"), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var response = await employee.GetAsync(MyAssignmentsUri($"?pharmacyId={setting.Pharmacy}&pageSize=101"), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task An_employee_sees_only_their_own_assignments_overdue_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var elsewhere = await PharmacyAsync(cancellationToken);
        var hygiene = await CourseAsync("Hygiene", cancellationToken);
        var firstAid = await CourseAsync("Erste Hilfe", cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var today = DayInGermany(TimeProvider.System.GetUtcNow());
        var inAMonth = today.AddDays(30);
        var annaHygiene = await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeA.PersonId, inAMonth, cancellationToken);
        var annaFirstAid = await AssignAsync(manager, setting.Pharmacy, firstAid.Id, setting.EmployeeA.PersonId, inAMonth, cancellationToken);
        await SetDueDateAsync(annaFirstAid, today.AddDays(-1), cancellationToken);
        var bertaHygiene = await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeB.PersonId, inAMonth, cancellationToken);
        await AssignAsync(manager, elsewhere.Pharmacy, hygiene.Id, elsewhere.EmployeeA.PersonId, inAMonth, cancellationToken);
        using var anna = await aspire.CreateHostClientAsync("employee-a");
        using var berta = await aspire.CreateHostClientAsync("employee-b");

        using var annaResponse = await anna.GetAsync(MyAssignmentsUri($"?pharmacyId={setting.Pharmacy}"), cancellationToken);
        using var bertaResponse = await berta.GetAsync(MyAssignmentsUri($"?pharmacyId={setting.Pharmacy}"), cancellationToken);

        var annas = await MyAssignmentsOfAsync(annaResponse, cancellationToken);
        annas.Items.ShouldBe(
        [
            new MyAssignment(annaFirstAid, firstAid.Id, firstAid.Title, "Overdue", today.AddDays(-1), firstAid.Version, false),
            new MyAssignment(annaHygiene, hygiene.Id, hygiene.Title, "Open", inAMonth, hygiene.Version, false),
        ]);
        annas.TotalCount.ShouldBe(2);
        var bertas = await MyAssignmentsOfAsync(bertaResponse, cancellationToken);
        bertas.Items.ShouldBe(
        [
            new MyAssignment(bertaHygiene, hygiene.Id, hygiene.Title, "Open", inAMonth, hygiene.Version, false),
        ]);
        bertas.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_running_session_the_current_version_is_started()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var hygiene = await CourseAsync("Hygiene", cancellationToken);
        var current = await PublishVersionAsync(hygiene, 2, cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeA.PersonId, DayInGermany(TimeProvider.System.GetUtcNow()).AddDays(30), cancellationToken);

        var row = await OnlyRowOfAsync(setting.Pharmacy, cancellationToken);

        row.StartCourseVersionId.ShouldBe(current);
        row.ContinuesSession.ShouldBeFalse();
    }

    [Fact]
    public async Task A_running_session_keeps_its_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var hygiene = await CourseAsync("Hygiene", cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeA.PersonId, DayInGermany(TimeProvider.System.GetUtcNow()).AddDays(30), cancellationToken);
        await DeliverAsync(SessionStartedType, SessionStarted(setting.EmployeeA.PersonId, hygiene, TimeProvider.System.GetUtcNow().AddMinutes(-1)), cancellationToken);
        await PublishVersionAsync(hygiene, 2, cancellationToken);

        var row = await OnlyRowOfAsync(setting.Pharmacy, cancellationToken);

        row.StartCourseVersionId.ShouldBe(hygiene.Version);
        row.ContinuesSession.ShouldBeTrue();
    }

    [Fact]
    public async Task A_session_ended_by_a_certificate_starts_the_current_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var hygiene = await CourseAsync("Hygiene", cancellationToken);
        var startedAt = TimeProvider.System.GetUtcNow().AddMinutes(-10);
        await DeliverAsync(SessionStartedType, SessionStarted(setting.EmployeeA.PersonId, hygiene, startedAt), cancellationToken);
        await DeliverAsync(IssuedType, Issued(setting.EmployeeA.PersonId, hygiene, startedAt.AddMinutes(1)), cancellationToken);
        var current = await PublishVersionAsync(hygiene, 2, cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var assignment = await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeA.PersonId, DayInGermany(TimeProvider.System.GetUtcNow()).AddDays(30), cancellationToken);

        var row = await OnlyRowOfAsync(setting.Pharmacy, cancellationToken);

        row.AssignmentId.ShouldBe(assignment);
        row.State.ShouldBe("Open");
        row.StartCourseVersionId.ShouldBe(current);
        row.ContinuesSession.ShouldBeFalse();
    }

    [Fact]
    public async Task Paging_returns_the_second_page()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var hygiene = await CourseAsync("Hygiene", cancellationToken);
        var firstAid = await CourseAsync("Erste Hilfe", cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var today = DayInGermany(TimeProvider.System.GetUtcNow());
        await AssignAsync(manager, setting.Pharmacy, hygiene.Id, setting.EmployeeA.PersonId, today.AddDays(30), cancellationToken);
        var later = await AssignAsync(manager, setting.Pharmacy, firstAid.Id, setting.EmployeeA.PersonId, today.AddDays(31), cancellationToken);
        using var anna = await aspire.CreateHostClientAsync("employee-a");

        using var response = await anna.GetAsync(MyAssignmentsUri($"?pharmacyId={setting.Pharmacy}&page=2&pageSize=1"), cancellationToken);

        var page = await MyAssignmentsOfAsync(response, cancellationToken);
        page.Items.ShouldHaveSingleItem().AssignmentId.ShouldBe(later);
        page.TotalCount.ShouldBe(2);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(1);
    }

    private static Uri MyAssignmentsUri(string query = "") => new($"/compliance/me/assignments{query}", UriKind.Relative);

    private static Uri AssignmentsUri(Guid pharmacy) => new($"/compliance/pharmacies/{pharmacy}/assignments", UriKind.Relative);

    /// <summary>The German calendar day of the instant, as Compliance reads due dates.</summary>
    private static DateOnly DayInGermany(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, GermanTime.Value).DateTime);

    private static async Task<MyAssignmentsPage> MyAssignmentsOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<MyAssignmentsPage>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
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

    private static MembershipEvent Started(Guid personId, Guid pharmacyId, string displayName, string jobFunction, string role) =>
        new(Guid.CreateVersion7(), personId, pharmacyId, displayName, jobFunction, role, TimeProvider.System.GetUtcNow(), NewMetadata(TimeProvider.System.GetUtcNow()));

    /// <summary>Learning's start of a session on the course's first version, which happened at the instant.</summary>
    private static SessionEvent SessionStarted(Guid personId, Course course, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), personId, course.Id, course.Version, NewMetadata(at));

    /// <summary>Certification's certificate for the course's first version, issued at the instant.</summary>
    private static IssuedEvent Issued(Guid personId, Course course, DateTimeOffset issuedAt) =>
        new(Guid.CreateVersion7(), personId, course.Id, course.Version, issuedAt, DayInGermany(issuedAt).AddMonths(24), NewMetadata(issuedAt));

    private static EventMetadata NewMetadata(DateTimeOffset occurredAt) => new(Guid.CreateVersion7(), occurredAt);

    private static PublishedEvent Published(Guid courseId, int number, string title) =>
        new(
            courseId,
            Guid.CreateVersion7(),
            number,
            title,
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata(TimeProvider.System.GetUtcNow()));

    /// <summary>employee-a's only row in the pharmacy.</summary>
    private async Task<MyAssignment> OnlyRowOfAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        using var anna = await aspire.CreateHostClientAsync("employee-a");
        using var response = await anna.GetAsync(MyAssignmentsUri($"?pharmacyId={pharmacy}"), cancellationToken);
        var page = await MyAssignmentsOfAsync(response, cancellationToken);
        page.TotalCount.ShouldBe(1);
        return page.Items.ShouldHaveSingleItem();
    }

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
        var published = Published(Guid.CreateVersion7(), 1, $"{name} {Guid.CreateVersion7()}");
        await DeliverAsync(PublishedType, published, cancellationToken);
        return new Course(published.CourseId, published.CourseVersionId, published.Title);
    }

    /// <summary>A further version of the course under the same title; answers its id.</summary>
    private async Task<Guid> PublishVersionAsync(Course course, int number, CancellationToken cancellationToken)
    {
        var published = Published(course.Id, number, course.Title);
        await DeliverAsync(PublishedType, published, cancellationToken);
        return published.CourseVersionId;
    }

    /// <summary>Moves the assignment's due date in Compliance's table, as #34's matrix tests do for an overdue one.</summary>
    private async Task SetDueDateAsync(Guid assignment, DateOnly dueDate, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("update compliance.assignments set due_date = @due where id = @id", connection);
        command.Parameters.AddWithValue("id", assignment);
        command.Parameters.AddWithValue("due", dueDate);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }

    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private sealed record Setting(Guid Pharmacy, MembershipEvent Manager, MembershipEvent EmployeeA, MembershipEvent EmployeeB);

    private sealed record Course(Guid Id, Guid Version, string Title);

    private sealed record MyAssignmentsPage(IReadOnlyList<MyAssignment> Items, int TotalCount, int Page, int PageSize);

    private sealed record MyAssignment(Guid AssignmentId, Guid CourseId, string CourseTitle, string State, DateOnly DueDate, Guid? StartCourseVersionId, bool ContinuesSession);

    private sealed record AssignmentAnswer(IReadOnlyList<AssignedLearner> Assigned, IReadOnlyList<RefusedLearner> Refused);

    private sealed record AssignedLearner(Guid PersonId, Guid AssignmentId);

    private sealed record RefusedLearner(Guid PersonId, string Code, string Message);

    private sealed record Problem(string Code);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record MembershipEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SessionEvent(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    private sealed record IssuedEvent(Guid CertificateId, Guid PersonId, Guid CourseId, Guid CourseVersionId, DateTimeOffset IssuedAt, DateOnly ValidUntil, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
