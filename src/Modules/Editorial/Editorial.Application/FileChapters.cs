using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The PDF or video chapter a file chapter command names; the endpoint checked its content against its kind.</summary>
internal static class FileChapters
{
    /// <summary>A PDF has no duration and a video has one; a text chapter is no file chapter. Anything else is a bug.</summary>
    public static void EnsureContentMatchesKind(ChapterKind kind, VideoDuration? duration) =>
        Ensure.That((int)kind, nameof(kind)).Satisfies(
            _ => (kind == ChapterKind.Pdf && duration is null) || (kind == ChapterKind.Video && duration is not null),
            "A file chapter is a PDF without a duration or a video with one.");

    public static Chapter Of(ChapterId id, ChapterTitle title, ChapterKind kind, BlobReference file, VideoDuration? duration) =>
        duration is { } length ? Chapter.OfVideo(id, title, file, length) : Chapter.OfPdf(id, title, file);
}
