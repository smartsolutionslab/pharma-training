namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The publisher check runs on every attempt, also on an envelope Wolverine loads again from its storage (docs/adr/0011,
/// D5): "the header travels with the envelope into the inbox and the dead letters, so a recovered or replayed envelope is
/// checked the same way". So the publisher header and where the envelope came from must survive Wolverine's storage, or
/// a legitimate event is refused as forged the second time it is tried. The failure that makes Compliance try a
/// legitimate event again is a course version whose number its course already has under another id: the consumer
/// throws until the stored version is gone.
/// </summary>
public class BrokerPublisherReloadTests(AspireFixture aspire)
{
    private const string CourseVersionPublished = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";

    private const string CourseVersionsById = "select count(*) from compliance.course_versions where id = @id";

    // Seven attempts take about a minute; the one after the conflict is gone comes within the next wait, at most 32 s.
    private static readonly TimeSpan HandledTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_legitimate_course_version_is_handled_after_a_scheduled_retry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseId = Guid.CreateVersion7();
        var stored = Published(courseId, 1);
        await ComplianceDatabase.DeliverAsync(aspire, CourseVersionPublished, stored, cancellationToken);
        var conflicting = Published(courseId, 1);
        var messageId = Guid.CreateVersion7();

        await ComplianceDatabase.SendAsync(aspire, CourseVersionPublished, conflicting, messageId, cancellationToken);
        await ComplianceDatabase.WaitUntilScheduledForRetryAsync(aspire, messageId, cancellationToken);
        await ComplianceDatabase.DeleteCourseVersionAsync(aspire, stored.CourseVersionId, cancellationToken);

        await ComplianceDatabase.WaitUntilHandledUnlessForgedAsync(aspire, messageId, HandledTimeout, cancellationToken);
        (await ComplianceDatabase.CountAsync(aspire, CourseVersionsById, conflicting.CourseVersionId, cancellationToken))
            .ShouldBe(1, $"Compliance did not store the course version {conflicting.CourseVersionId} it handled after a retry.");
    }

    private static PublishedEvent Published(Guid courseId, int versionNumber) =>
        new(
            courseId,
            Guid.CreateVersion7(),
            versionNumber,
            $"Kühlkette {versionNumber} {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
