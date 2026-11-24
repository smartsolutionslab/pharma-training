using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
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

    /// <summary>
    /// The chapter: a video, or a PDF with what was extracted from its pages, which a PDF chapter always has; a missing
    /// one is a bug.
    /// </summary>
    public static Chapter Of(ChapterId id, ChapterTitle title, BlobReference file, VideoDuration? duration, PdfChapterText? text)
    {
        if (duration is { } length)
        {
            return Chapter.OfVideo(id, title, file, length);
        }

        return text is { } extracted
            ? Chapter.OfPdf(id, title, file, extracted.Extraction, extracted.Text)
            : throw new UnreachableException("A PDF chapter is recorded with what was extracted from its pages.");
    }
}
