using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Issuing a certificate through the running application, as the other modules' events reach Certification:
/// Organisation's membership, Editorial's published course version and Assessment's passed test, each built here as its
/// publisher sends it, and the certificates Certification issued for an attempt, read from its own table.
/// </summary>
internal static class CertificationIssuing
{
    public const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    public const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    public const string ChangedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";
    public const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    public const string PassedType = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestPassedV1";

    /// <summary>A person of their own starts a membership and passes a course version of its own, published with the validity; answers the certificate issued.</summary>
    public static async Task<CertificateRow> IssueAsync(AspireFixture aspire, int validityMonths, DateTimeOffset passedAt, CancellationToken cancellationToken)
    {
        var started = Started(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());
        var published = Published(validityMonths);
        var passed = Passed(started, published, passedAt);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, PassedType, passed, cancellationToken);
        return (await CertificatesAsync(aspire, passed.AttemptId, cancellationToken)).ShouldHaveSingleItem();
    }

    public static StartedEvent Started(Guid personId, DateTimeOffset startedAt) =>
        new(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), $"Anna Berger {Guid.CreateVersion7()}", "Pta", "Employee", startedAt, NewMetadata());

    /// <summary>The person starts a membership in the pharmacy, in the role (<c>Manager</c> or <c>Employee</c>), at the instant.</summary>
    public static StartedEvent StartedIn(Guid personId, Guid pharmacyId, string role, DateTimeOffset startedAt) =>
        new(Guid.CreateVersion7(), personId, pharmacyId, $"Anna Berger {Guid.CreateVersion7()}", "Pta", role, startedAt, NewMetadata());

    public static EndedEvent Ended(StartedEvent started) =>
        EndedAt(started, TimeProvider.System.GetUtcNow());

    public static EndedEvent EndedAt(StartedEvent started, DateTimeOffset endedAt) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, endedAt, NewMetadata());

    /// <summary>Organisation changes the membership's role at the version; the first change is version 2.</summary>
    public static ChangedEvent Changed(StartedEvent started, string role, int version) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, started.DisplayName, started.JobFunction, role, version, NewMetadata());

    public static PublishedEvent Published(int validityMonths = 12) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            3,
            $"Hygiene {Guid.CreateVersion7()}",
            validityMonths,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    public static PassedEvent Passed(StartedEvent started, PublishedEvent published, DateTimeOffset passedAt) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), started.PersonId, published.CourseId, published.CourseVersionId, 90, passedAt, NewMetadata());

    public static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>The certificates Certification issued for the attempt, from its own table.</summary>
    public static async Task<List<CertificateRow>> CertificatesAsync(AspireFixture aspire, Guid attemptId, CancellationToken cancellationToken)
    {
        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select id, holder_id, holder_name, course_id, course_version_id, course_title, course_version_number, score_percent, issued_at, valid_until, verification_code, document_state, document_location
            from certification.certificates where attempt_id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", attemptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<CertificateRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CertificateRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateOnly>(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return rows;
    }

    public sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    public sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    public sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    public sealed record ChangedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, int Version, EventMetadata Metadata);

    public sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    public sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    public sealed record PassedEvent(Guid AttemptId, Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, int ScorePercent, DateTimeOffset PassedAt, EventMetadata Metadata);

    public sealed record CertificateRow(
        Guid Id,
        Guid HolderId,
        string HolderName,
        Guid CourseId,
        Guid CourseVersionId,
        string CourseTitle,
        int CourseVersionNumber,
        int ScorePercent,
        DateTimeOffset IssuedAt,
        DateOnly ValidUntil,
        string VerificationCode,
        string DocumentState,
        string? DocumentLocation);
}
