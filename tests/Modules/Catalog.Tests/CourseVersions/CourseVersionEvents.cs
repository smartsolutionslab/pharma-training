using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.CourseVersions;

/// <summary>Editorial's events as they arrive: published by its unit of work, so each carries its metadata.</summary>
internal static class CourseVersionEvents
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A text, a video and a PDF chapter, in that order of position but not of the list.</summary>
    public static IReadOnlyList<CourseChapterV1> ThreeChapters() =>
    [
        new CourseChapterV1(Guid.CreateVersion7(), 2, "Lagerung", ChapterKindV1.Video, 420, null, "blobs/lagerung.mp4"),
        new CourseChapterV1(Guid.CreateVersion7(), 1, "Grundlagen", ChapterKindV1.Text, null, "# Grundlagen\n\nKühlkette **immer** einhalten.", null),
        new CourseChapterV1(Guid.CreateVersion7(), 3, "Checkliste", ChapterKindV1.Pdf, null, null, "blobs/checkliste.pdf"),
    ];

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, int versionNumber, string title) =>
        Published(courseId, courseVersionId, versionNumber, title, ThreeChapters());

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, int versionNumber, string title, IReadOnlyList<CourseChapterV1> chapters) =>
        new(courseId, courseVersionId, versionNumber, title, 24, chapters) { Metadata = NewMetadata() };

    public static CourseVersionRetiredV1 Retired(Guid courseId, Guid courseVersionId) =>
        new(courseId, courseVersionId) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), PublishedAt);
}
