using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A yearly assignment in Compliance and the certificate that completes it, as their events and the edge make them:
/// a pharmacy of its own (developer as its manager, employee-a and employee-b as its employees), a course of its own,
/// the course assigned to employee-a every 12 months, and Certification's <c>CertificateIssuedV1</c> issued after the
/// assignment and before it is delivered, since Compliance takes no instant from the future. Then the follow-up the
/// completion leads to, read from Compliance's tables (#31).
/// </summary>
internal sealed class ComplianceFollowUps(AspireFixture aspire)
{
    public const string IssuedType = "SmartSolutionsLab.PharmaTraining.Contracts.Certification.CertificateIssuedV1";

    public const string FollowUpRequestedType = "SmartSolutionsLab.PharmaTraining.Contracts.Compliance.AssignmentFollowUpRequestedV1";

    public const string AssignedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Compliance.CourseAssignedV1";

    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const int Yearly = 12;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeZoneInfo GermanTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly OrganisationDatabase organisation = new(aspire);

    /// <summary>The German calendar day of the instant, the day an interval counts from.</summary>
    public static DateOnly DayInGermany(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, GermanTime).DateTime);

    public static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>A pharmacy and a course of their own, and the course assigned through the edge to employee-a every 12 months.</summary>
    public async Task<Recurring> AssignYearlyAsync(CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        var manager = await organisation.SubjectOfAsync("developer");
        var employeeA = await organisation.SubjectOfAsync("employee-a");
        var employeeB = await organisation.SubjectOfAsync("employee-b");
        await DeliverAsync(StartedType, Started(manager, pharmacy, await organisation.NameOfAsync("developer"), "Pharmacist", "Manager"), cancellationToken);
        await DeliverAsync(StartedType, Started(employeeA, pharmacy, await organisation.NameOfAsync("employee-a"), "Pta", "Employee"), cancellationToken);
        await DeliverAsync(StartedType, Started(employeeB, pharmacy, await organisation.NameOfAsync("employee-b"), "Pka", "Employee"), cancellationToken);
        var course = Published(Guid.CreateVersion7());
        await DeliverAsync(PublishedType, course, cancellationToken);

        using var client = await aspire.CreateHostClientAsync("developer");
        using var response = await client.PostAsJsonAsync(
            new Uri($"/compliance/pharmacies/{pharmacy}/assignments", UriKind.Relative),
            new { courseId = course.CourseId, personIds = new[] { employeeA }, dueDate = Today().AddDays(30), recurrenceMonths = (int?)Yearly },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var answer = JsonSerializer.Deserialize<AssignmentAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
        var assignment = answer.Assigned.ShouldHaveSingleItem().AssignmentId;

        return new Recurring(pharmacy, employeeA, manager, course.CourseId, course.CourseVersionId, assignment, await AssignedAtAsync(assignment, cancellationToken));
    }

    /// <summary>
    /// Certification's certificate for the learner and course, issued on the first whole second after the assignment
    /// and delivered once that second has passed; answers the event, to deliver it again.
    /// </summary>
    public async Task<IssuedEvent> IssueAsync(Recurring recurring, CancellationToken cancellationToken)
    {
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(recurring.AssignedAt.ToUnixTimeSeconds() + 1);
        while (TimeProvider.System.GetUtcNow() <= issuedAt)
        {
            await Task.Delay(PollInterval, cancellationToken);
        }

        var issued = new IssuedEvent(
            Guid.CreateVersion7(),
            recurring.Learner,
            recurring.Course,
            recurring.CourseVersion,
            issuedAt,
            DateOnly.FromDateTime(issuedAt.UtcDateTime).AddMonths(24),
            NewMetadata());
        await DeliverAsync(IssuedType, issued, cancellationToken);
        return issued;
    }

    /// <summary>Sends the event under a new envelope id and waits until Compliance's inbox has handled it.</summary>
    public Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    /// <summary>Waits until Compliance stored an assignment that follows up the one given, and answers it.</summary>
    public async Task<FollowUpRow> WaitForFollowUpAsync(Guid assignment, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "select id, due_date, status, recurrence_months, assigned_by, assigned_at from compliance.assignments where follows_assignment_id = @id",
                connection);
            command.Parameters.AddWithValue("id", assignment);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new FollowUpRow(
                        reader.GetGuid(0),
                        reader.GetFieldValue<DateOnly>(1),
                        reader.GetString(2),
                        await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetInt32(3),
                        reader.GetGuid(4),
                        reader.GetFieldValue<DateTimeOffset>(5));
                }
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Compliance stored no follow-up of assignment {assignment} within {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Waits until Compliance's inbox holds no follow-up request it has not handled yet, so a request a redelivery
    /// would have made has been dealt with.
    /// </summary>
    public async Task WaitUntilNoFollowUpRequestIsPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "select count(*) from wolverine.wolverine_incoming_envelopes where message_type like @type and status <> 'Handled'",
                connection);
            command.Parameters.AddWithValue("type", "%AssignmentFollowUpRequestedV1");
            var pending = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (pending == 0)
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Compliance's inbox still held {pending} follow-up requests not handled after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>The assignments that follow up the one given.</summary>
    public Task<long> FollowUpCountAsync(Guid assignment, CancellationToken cancellationToken) =>
        ComplianceDatabase.CountAsync(aspire, "select count(*) from compliance.assignments where follows_assignment_id = @id", assignment, cancellationToken);

    /// <summary>The status of every assignment of the recurring one's learner, pharmacy and course, by id.</summary>
    public async Task<Dictionary<Guid, string>> AssignmentsAsync(Recurring recurring, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select id, status from compliance.assignments where learner_id = @learner and pharmacy_id = @pharmacy and course_id = @course",
            connection);
        command.Parameters.AddWithValue("learner", recurring.Learner);
        command.Parameters.AddWithValue("pharmacy", recurring.Pharmacy);
        command.Parameters.AddWithValue("course", recurring.Course);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Dictionary<Guid, string> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetGuid(0), reader.GetString(1));
        }

        return rows;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

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

    private async Task<DateTimeOffset> AssignedAtAsync(Guid assignment, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select assigned_at from compliance.assignments where id = @id", connection);
        command.Parameters.AddWithValue("id", assignment);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"Compliance stored no assignment {assignment}.");
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    /// <summary>The yearly assignment: its pharmacy, learner, assigner (the developer's subject), course and when it was made.</summary>
    public sealed record Recurring(Guid Pharmacy, Guid Learner, Guid AssignedBy, Guid Course, Guid CourseVersion, Guid Assignment, DateTimeOffset AssignedAt);

    public sealed record FollowUpRow(Guid Id, DateOnly DueDate, string Status, int? RecurrenceMonths, Guid AssignedBy, DateTimeOffset AssignedAt);

    public sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    public sealed record IssuedEvent(Guid CertificateId, Guid PersonId, Guid CourseId, Guid CourseVersionId, DateTimeOffset IssuedAt, DateOnly ValidUntil, EventMetadata Metadata);

    private sealed record AssignmentAnswer(IReadOnlyList<AssignedLearner> Assigned);

    private sealed record AssignedLearner(Guid PersonId, Guid AssignmentId);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
