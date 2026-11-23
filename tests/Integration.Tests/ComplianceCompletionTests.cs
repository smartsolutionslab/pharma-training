using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Certification's <c>CertificateIssuedV1</c> completes, through the broker, every active assignment of the person for
/// the course in every pharmacy, and Compliance announces each completion once as <c>AssignmentCompletedV1</c>, late
/// when the certificate's day in Germany lies after the due date. The same certificate again, under a new envelope or a
/// new event identifier, changes nothing; a certificate issued before the assignment does not fulfil it. Members and
/// courses reach Compliance as their events; each test has pharmacies and a course of its own.
/// </summary>
public class ComplianceCompletionTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string IssuedType = "SmartSolutionsLab.PharmaTraining.Contracts.Certification.CertificateIssuedV1";
    private const string CompletedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Compliance.AssignmentCompletedV1";

    private readonly OrganisationDatabase organisation = new(aspire);

    /// <summary>
    /// Counts on a queue of the test's own, up to a marker certificate for another person delivered after the
    /// redeliveries: Compliance sends its announcements in the order it commits them, so the marker's comes after any a
    /// redelivery would have sent.
    /// </summary>
    [Fact]
    public async Task One_certificate_completes_the_learners_assignments_in_two_pharmacies_and_announces_each_once_with_its_late_flag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pharmacyA = await PharmacyAsync(cancellationToken);
        var pharmacyB = await PharmacyAsync(cancellationToken);
        var course = Published(Guid.CreateVersion7());
        await DeliverAsync(PublishedType, course, cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        // A warm-up completion, so Compliance has sent an announcement and Wolverine has declared its exchange.
        await AssignAsync(manager, pharmacyA.Pharmacy, course.CourseId, pharmacyA.EmployeeB, Today().AddDays(30), cancellationToken);
        await DeliverAsync(IssuedType, Issued(pharmacyA.EmployeeB, course, WholeSecondAfterNow()), cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CompletedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, CompletedExchange, cancellationToken);

        var inA = await AssignAsync(manager, pharmacyA.Pharmacy, course.CourseId, pharmacyA.EmployeeA, Today().AddDays(30), cancellationToken);
        var inB = await AssignAsync(manager, pharmacyB.Pharmacy, course.CourseId, pharmacyB.EmployeeA, Today().AddDays(1), cancellationToken);
        var issuedAt = WholeSecondAfterNow().AddDays(3);
        var issued = Issued(pharmacyA.EmployeeA, course, issuedAt);
        await DeliverAsync(IssuedType, issued, cancellationToken);

        // The same event again under another envelope id, so the inbox lets it through to the consumer, then
        // published again by Certification under an event identifier of its own.
        await DeliverAsync(IssuedType, issued, cancellationToken);
        await DeliverAsync(IssuedType, issued with { Metadata = NewMetadata() }, cancellationToken);
        await AssignAsync(manager, pharmacyB.Pharmacy, course.CourseId, pharmacyB.EmployeeB, Today().AddDays(30), cancellationToken);
        await DeliverAsync(IssuedType, Issued(pharmacyB.EmployeeB, course, WholeSecondAfterNow()), cancellationToken);

        var announced = await Broker.ReceiveUntilAsync<JsonElement>(
            channel,
            queue,
            received => IdOf(received, "personId") == pharmacyB.EmployeeB && IdOf(received, "pharmacyId") == pharmacyB.Pharmacy,
            cancellationToken);
        var completions = announced.Where(received => IdOf(received, "personId") == pharmacyA.EmployeeA && IdOf(received, "courseId") == course.CourseId).ToList();
        completions.Select(received => IdOf(received, "pharmacyId")).ShouldBe([pharmacyA.Pharmacy, pharmacyB.Pharmacy], ignoreOrder: true);
        var completedInA = completions.Single(received => IdOf(received, "pharmacyId") == pharmacyA.Pharmacy);
        var completedInB = completions.Single(received => IdOf(received, "pharmacyId") == pharmacyB.Pharmacy);
        IdOf(completedInA, "assignmentId").ShouldBe(inA);
        IdOf(completedInB, "assignmentId").ShouldBe(inB);
        foreach (var completed in completions)
        {
            IdOf(completed, "certificateId").ShouldBe(issued.CertificateId);
            completed.GetProperty("completedAt").GetDateTimeOffset().ShouldBe(issuedAt);
        }

        completedInA.GetProperty("late").GetBoolean().ShouldBeFalse();
        completedInB.GetProperty("late").GetBoolean().ShouldBeTrue();

        var rows = await AssignmentsAsync(pharmacyA.EmployeeA, course.CourseId, cancellationToken);
        rows.Select(row => row.PharmacyId).ShouldBe([pharmacyA.Pharmacy, pharmacyB.Pharmacy], ignoreOrder: true);
        rows.ShouldAllBe(row => row.Status == "Completed" && row.CertificateId == issued.CertificateId && row.CompletedAt == issuedAt);
    }

    [Fact]
    public async Task A_certificate_issued_before_the_assignment_leaves_it_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = Published(Guid.CreateVersion7());
        await DeliverAsync(PublishedType, course, cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        await AssignAsync(manager, setting.Pharmacy, course.CourseId, setting.EmployeeA, Today().AddDays(30), cancellationToken);

        await DeliverAsync(IssuedType, Issued(setting.EmployeeA, course, WholeSecondAfterNow().AddHours(-1)), cancellationToken);

        var row = (await AssignmentsAsync(setting.EmployeeA, course.CourseId, cancellationToken)).ShouldHaveSingleItem();
        row.Status.ShouldBe("Open");
        row.CertificateId.ShouldBeNull();
        row.CompletedAt.ShouldBeNull();
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

    /// <summary>The next whole second, so the instant survives the database and JSON unchanged.</summary>
    private static DateTimeOffset WholeSecondAfterNow() =>
        DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() + 1);

    private static Guid IdOf(JsonElement received, string property) => received.GetProperty(property).GetGuid();

    private static Uri AssignmentsUri(Guid pharmacy) => new($"/compliance/pharmacies/{pharmacy}/assignments", UriKind.Relative);

    /// <summary>Assigns the course to one learner through the edge and answers the new assignment's id.</summary>
    private static async Task<Guid> AssignAsync(HttpClient manager, Guid pharmacy, Guid course, Guid learner, DateOnly dueDate, CancellationToken cancellationToken)
    {
        using var response = await manager.PostAsJsonAsync(
            AssignmentsUri(pharmacy),
            new { courseId = course, personIds = new[] { learner }, dueDate, recurrenceMonths = (int?)null },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var answer = JsonSerializer.Deserialize<AssignmentAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
        return answer.Assigned.ShouldHaveSingleItem().AssignmentId;
    }

    private static StartedEvent Started(Guid personId, Guid pharmacyId, string displayName, string jobFunction, string role) =>
        new(Guid.CreateVersion7(), personId, pharmacyId, displayName, jobFunction, role, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static PublishedEvent Published(Guid courseId) =>
        new(
            courseId,
            Guid.CreateVersion7(),
            1,
            $"Hygiene {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static IssuedEvent Issued(Guid personId, PublishedEvent course, DateTimeOffset issuedAt) =>
        new(Guid.CreateVersion7(), personId, course.CourseId, course.CourseVersionId, issuedAt, DateOnly.FromDateTime(issuedAt.UtcDateTime).AddMonths(24), NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>A pharmacy of its own in Compliance: developer as its manager, employee-a and employee-b as its employees.</summary>
    private async Task<Setting> PharmacyAsync(CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        var manager = await organisation.SubjectOfAsync("developer");
        var employeeA = await organisation.SubjectOfAsync("employee-a");
        var employeeB = await organisation.SubjectOfAsync("employee-b");
        await DeliverAsync(StartedType, Started(manager, pharmacy, await organisation.NameOfAsync("developer"), "Pharmacist", "Manager"), cancellationToken);
        await DeliverAsync(StartedType, Started(employeeA, pharmacy, await organisation.NameOfAsync("employee-a"), "Pta", "Employee"), cancellationToken);
        await DeliverAsync(StartedType, Started(employeeB, pharmacy, await organisation.NameOfAsync("employee-b"), "Pka", "Employee"), cancellationToken);
        return new Setting(pharmacy, employeeA, employeeB);
    }

    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private async Task<List<AssignmentRow>> AssignmentsAsync(Guid learner, Guid course, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select pharmacy_id, status, certificate_id, completed_at from compliance.assignments where learner_id = @learner and course_id = @course",
            connection);
        command.Parameters.AddWithValue("learner", learner);
        command.Parameters.AddWithValue("course", course);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<AssignmentRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AssignmentRow(
                reader.GetGuid(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetGuid(2),
                await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return rows;
    }

    private sealed record Setting(Guid Pharmacy, Guid EmployeeA, Guid EmployeeB);

    private sealed record AssignmentAnswer(IReadOnlyList<AssignedLearner> Assigned);

    private sealed record AssignedLearner(Guid PersonId, Guid AssignmentId);

    private sealed record AssignmentRow(Guid PharmacyId, string Status, Guid? CertificateId, DateTimeOffset? CompletedAt);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record IssuedEvent(Guid CertificateId, Guid PersonId, Guid CourseId, Guid CourseVersionId, DateTimeOffset IssuedAt, DateOnly ValidUntil, EventMetadata Metadata);
}
