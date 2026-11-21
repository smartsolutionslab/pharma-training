using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>One chapter of a course version: Markdown text, a PDF or a video.</summary>
public sealed class Chapter : Entity<ChapterId>
{
    private Chapter(ChapterId id, ChapterTitle title, ChapterKind kind)
        : base(id)
    {
        Title = title;
        Kind = kind;
    }

    public ChapterTitle Title { get; private set; }

    public ChapterKind Kind { get; private set; }

    public MarkdownText? Text { get; private set; }

    public BlobReference? File { get; private set; }

    public VideoDuration? Duration { get; private set; }

    public static Chapter OfText(ChapterId id, ChapterTitle title, MarkdownText text) => new(id, title, ChapterKind.Text) { Text = text };

    public static Chapter OfPdf(ChapterId id, ChapterTitle title, BlobReference pdf) => new(id, title, ChapterKind.Pdf) { File = pdf };

    public static Chapter OfVideo(ChapterId id, ChapterTitle title, BlobReference video, VideoDuration duration) =>
        new(id, title, ChapterKind.Video) { File = video, Duration = duration };

    internal Chapter CopyFor(ChapterId id) => new(id, Title, Kind) { Text = Text, File = File, Duration = Duration };

    /// <summary>
    /// Takes the replacement's content in place, so the stored chapter stays the one instance with its id (EF Core
    /// tracks one instance per key).
    /// </summary>
    internal void Take(Chapter replacement)
    {
        Title = replacement.Title;
        Kind = replacement.Kind;
        Text = replacement.Text;
        File = replacement.File;
        Duration = replacement.Duration;
    }
}
