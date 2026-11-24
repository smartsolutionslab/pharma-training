using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// What the AI content review sends, worked out from the version before the model is called (ADR 0009), whatever state
/// the version is in: its chapters with text (a text chapter's Markdown, a PDF's extracted text), numbered together from
/// one in the version's order. Video chapters and PDF chapters without text are left out, and the PDF chapters reviewed
/// from text cut at a limit are named, each in the version's order, so the editor knows (#302). Titles and text chapters
/// are sent whole; too many of them are refused. The PDFs share what they leave of <see cref="MaxCharacters"/>: a PDF
/// shorter than its even share is sent whole, the longer ones are cut to an even share of the rest and named as cut, and
/// a PDF nothing is left for is left out, so long PDFs never cost the version its review.
/// </summary>
public sealed record ContentReviewRequest(
    IReadOnlyList<ContentReviewChapter> Chapters,
    IReadOnlyList<ChapterId> LeftOutChapters,
    IReadOnlyList<ChapterId> TruncatedChapters)
{
    /// <summary>The most characters of chapter titles and texts one request sends, about 100k tokens.</summary>
    public const int MaxCharacters = 400_000;

    public static Result<ContentReviewRequest, ApiError> For(CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        List<(ChapterId Id, ChapterTitle Title, ContentReviewSource Source, string Text)> texts = [];
        List<ChapterId> leftOut = [];
        List<ChapterId> truncated = [];
        foreach (var chapter in version.Chapters)
        {
            if (chapter is { Kind: ChapterKind.Text, Text: { } markdown })
            {
                texts.Add((chapter.Id, chapter.Title, ContentReviewSource.Text, markdown.Value));
            }
            else if (chapter is { Kind: ChapterKind.Pdf, Extraction: PdfTextExtraction.Complete or PdfTextExtraction.Truncated, ExtractedText: { } extracted })
            {
                texts.Add((chapter.Id, chapter.Title, ContentReviewSource.Pdf, extracted.Value));
                if (chapter.Extraction == PdfTextExtraction.Truncated)
                {
                    truncated.Add(chapter.Id);
                }
            }
            else
            {
                leftOut.Add(chapter.Id);
            }
        }

        if (texts.Count == 0)
        {
            return Failure(ContentReviewFailures.NoTextChapters());
        }

        var fixedCharacters = texts.Sum(chapter => (long)chapter.Title.Value.Length + (chapter.Source == ContentReviewSource.Text ? chapter.Text.Length : 0));
        if (fixedCharacters > MaxCharacters)
        {
            return Failure(ContentReviewFailures.TooMuchContent());
        }

        var allowances = PdfAllowances(texts, MaxCharacters - fixedCharacters);
        List<ContentReviewChapter> chapters = [];
        foreach (var (id, title, source, text) in texts)
        {
            var sent = text;
            if (source == ContentReviewSource.Pdf)
            {
                sent = Cut(text, allowances[id]);
                if (sent.Length == 0)
                {
                    leftOut.Add(id);
                    truncated.Remove(id);
                    continue;
                }

                if (sent.Length < text.Length && !truncated.Contains(id))
                {
                    truncated.Add(id);
                }
            }

            chapters.Add(new ContentReviewChapter(chapters.Count + 1, id, title, source, sent));
        }

        if (chapters.Count == 0)
        {
            return Failure(ContentReviewFailures.NoTextChapters());
        }

        return Success(new ContentReviewRequest(chapters, InVersionOrder(version, leftOut), InVersionOrder(version, truncated)));
    }

    /// <summary>
    /// How many characters of its text each PDF chapter may send out of <paramref name="budget"/>: shortest first, a PDF
    /// within an even share of what is left keeps its whole text, and the rest split what remains evenly.
    /// </summary>
    private static Dictionary<ChapterId, int> PdfAllowances(
        IReadOnlyList<(ChapterId Id, ChapterTitle Title, ContentReviewSource Source, string Text)> texts,
        long budget)
    {
        var pdfs = texts.Where(chapter => chapter.Source == ContentReviewSource.Pdf).OrderBy(chapter => chapter.Text.Length).ToList();
        Dictionary<ChapterId, int> allowances = [];
        var left = budget;
        for (var index = 0; index < pdfs.Count; index++)
        {
            var share = left / (pdfs.Count - index);
            var allowance = (int)Math.Min(pdfs[index].Text.Length, share);
            allowances[pdfs[index].Id] = allowance;
            left -= allowance;
        }

        return allowances;
    }

    /// <summary>The text up to <paramref name="length"/> characters, never ending in the first half of a surrogate pair.</summary>
    private static string Cut(string text, int length)
    {
        if (length >= text.Length)
        {
            return text;
        }

        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

    private static List<ChapterId> InVersionOrder(CourseVersion version, List<ChapterId> chapters) =>
        [.. version.Chapters.Select(chapter => chapter.Id).Where(chapters.Contains)];
}
