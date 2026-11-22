using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;

/// <summary>Editorial's events as they arrive: published by its unit of work, so each carries its metadata.</summary>
internal static class CourseVersionEvents
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, int versionNumber, string title) =>
        Published(courseId, courseVersionId, versionNumber, title, 24);

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, int versionNumber, string title, int validityMonths) =>
        new(courseId, courseVersionId, versionNumber, title, validityMonths, [new CourseChapterV1(Guid.CreateVersion7(), 1, "Grundlagen", ChapterKindV1.Text, null, "# Grundlagen", null)]) { Metadata = NewMetadata() };

    public static CourseVersionRetiredV1 Retired(Guid courseId, Guid courseVersionId) =>
        new(courseId, courseVersionId) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), PublishedAt);
}
