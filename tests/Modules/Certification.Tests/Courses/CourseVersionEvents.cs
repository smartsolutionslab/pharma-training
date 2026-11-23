using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Courses;

/// <summary>Editorial's events as they arrive: published by its unit of work, so each carries its metadata.</summary>
internal static class CourseVersionEvents
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, string title) =>
        Published(courseId, courseVersionId, title, 12);

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, string title, int validityMonths) =>
        new(courseId, courseVersionId, 1, title, validityMonths, [new CourseChapterV1(Guid.CreateVersion7(), 1, "Grundlagen", ChapterKindV1.Text, null, "# Grundlagen", null)]) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), PublishedAt);
}
