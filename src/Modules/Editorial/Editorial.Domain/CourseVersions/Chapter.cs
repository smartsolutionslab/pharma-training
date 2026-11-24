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

    /// <summary>
    /// What was extracted from a PDF chapter's pages when it was recorded; null for text and video chapters, and for a
    /// PDF chapter recorded before extraction existed (#157).
    /// </summary>
    public PdfTextExtraction? Extraction { get; private set; }

    /// <summary>A PDF chapter's extracted text, present exactly when its extraction is complete or truncated.</summary>
    public PdfText? ExtractedText { get; private set; }

    public static Chapter OfText(ChapterId id, ChapterTitle title, MarkdownText text) => new(id, title, ChapterKind.Text) { Text = text };

    /// <summary>A PDF chapter with what was extracted from its pages: a text exactly when the extraction found one.</summary>
    public static Chapter OfPdf(ChapterId id, ChapterTitle title, BlobReference pdf, PdfTextExtraction extraction, PdfText? text)
    {
        Ensure.That((int)extraction, nameof(extraction)).Satisfies(
            _ => (extraction == PdfTextExtraction.NoText) == (text is null),
            "A PDF chapter has an extracted text exactly when its extraction found one.");
        return new(id, title, ChapterKind.Pdf) { File = pdf, Extraction = extraction, ExtractedText = text };
    }

    public static Chapter OfVideo(ChapterId id, ChapterTitle title, BlobReference video, VideoDuration duration) =>
        new(id, title, ChapterKind.Video) { File = video, Duration = duration };

    internal Chapter CopyFor(ChapterId id) => new(id, Title, Kind) { Text = Text, File = File, Duration = Duration, Extraction = Extraction, ExtractedText = ExtractedText };

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
        Extraction = replacement.Extraction;
        ExtractedText = replacement.ExtractedText;
    }
}
