using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>Editorial's <see cref="CourseVersionPublishedV1"/> as it arrives: published by its unit of work, so it carries its metadata.</summary>
internal static class CourseVersionEvents
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    public static CourseVersionPublishedV1 Published(Guid courseId, Guid courseVersionId, int versionNumber, IReadOnlyList<CourseChapterV1> chapters) =>
        new(courseId, courseVersionId, versionNumber, "Kühlkette in der Apotheke", 24, chapters) { Metadata = new EventMetadata(Guid.CreateVersion7(), PublishedAt) };

    public static CourseChapterV1 Text(int position) => new(Guid.CreateVersion7(), position, "Grundlagen", ChapterKindV1.Text, null, "# Grundlagen", null);

    public static CourseChapterV1 Pdf(int position) => new(Guid.CreateVersion7(), position, "Handbuch", ChapterKindV1.Pdf, null, null, "chapters/handbuch.pdf");

    public static CourseChapterV1 Video(int position, int seconds) => new(Guid.CreateVersion7(), position, "Film", ChapterKindV1.Video, seconds, null, "chapters/film.mp4");
}
