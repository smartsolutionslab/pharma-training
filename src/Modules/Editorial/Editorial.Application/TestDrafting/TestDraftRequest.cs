using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// What the AI test draft asks the model for, worked out from the version before the model is called (ADR 0008): its
/// chapters with text (a text chapter's Markdown, a PDF's extracted text), numbered together from one in the version's
/// order, and the blueprint's count less the questions the draft keeps (every one but the AI-drafted ones that are
/// untouched, never approved and unreviewed, #245), split evenly over them with the remainder on the first ones. Video
/// chapters and PDF chapters without text are left out, and the PDF chapters drafted from text cut at a limit are named,
/// each in the version's order, so the editor knows. Text chapters are sent whole; too many of them are refused. The PDFs
/// share what the titles and text chapters leave of <see cref="MaxCharacters"/>: a PDF shorter than its even share is
/// sent whole, the longer ones are cut to an even share of the rest and named as cut, and a PDF nothing is left for is
/// left out, so long PDFs never cost the version its draft (#157).
/// </summary>
public sealed record TestDraftRequest(
    IReadOnlyList<TestDraftChapter> Chapters,
    QuestionCount Questions,
    IReadOnlyList<ChapterId> LeftOutChapters,
    IReadOnlyList<ChapterId> TruncatedChapters)
{
    /// <summary>
    /// The most characters of chapter titles and texts one request sends, about 100k tokens, counted before JSON escaping,
    /// which can lengthen them, so the message itself can be longer.
    /// </summary>
    public const int MaxCharacters = 400_000;

    public static Result<TestDraftRequest, ApiError> For(CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        if (version.Status != CourseVersionStatus.Draft)
        {
            return Failure(CourseVersionFailures.From(CourseVersionErrors.NotADraft(version.Id, version.Status)));
        }

        List<(ChapterId Id, ChapterTitle Title, TestDraftSource Source, string Text)> texts = [];
        List<ChapterId> leftOut = [];
        List<ChapterId> truncated = [];
        foreach (var chapter in version.Chapters)
        {
            if (chapter is { Kind: ChapterKind.Text, Text: { } markdown })
            {
                texts.Add((chapter.Id, chapter.Title, TestDraftSource.Text, markdown.Value));
            }
            else if (chapter is { Kind: ChapterKind.Pdf, Extraction: PdfTextExtraction.Complete or PdfTextExtraction.Truncated, ExtractedText: { } extracted })
            {
                texts.Add((chapter.Id, chapter.Title, TestDraftSource.Pdf, extracted.Value));
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
            return Failure(CourseVersionFailures.NoTextChapters());
        }

        // Every question the draft keeps counts: approved ones, copied or hand-written ones even while unreviewed, and AI-drafted
        // ones an editor edited or approved once (#245).
        var kept = version.Questions.Count(question => !question.IsReplacedByAiDraft());
        var requested = version.Blueprint.Questions.Value - kept;
        if (requested <= 0)
        {
            return Failure(CourseVersionFailures.NothingToDraft());
        }

        var fixedCharacters = texts.Sum(chapter => (long)chapter.Title.Value.Length + (chapter.Source == TestDraftSource.Text ? chapter.Text.Length : 0));
        if (fixedCharacters > MaxCharacters)
        {
            return Failure(CourseVersionFailures.TooMuchContentToDraft());
        }

        var allowances = PdfAllowances(texts, MaxCharacters - fixedCharacters);
        List<(ChapterId Id, ChapterTitle Title, TestDraftSource Source, string Text)> sent = [];
        foreach (var chapter in texts)
        {
            if (chapter.Source == TestDraftSource.Text)
            {
                sent.Add(chapter);
                continue;
            }

            var allowed = Cut(chapter.Text, allowances[chapter.Id]);
            if (allowed.Length == 0)
            {
                leftOut.Add(chapter.Id);
                truncated.Remove(chapter.Id);
                continue;
            }

            if (allowed.Length < chapter.Text.Length && !truncated.Contains(chapter.Id))
            {
                truncated.Add(chapter.Id);
            }

            sent.Add(chapter with { Text = allowed });
        }

        if (sent.Count == 0)
        {
            return Failure(CourseVersionFailures.NoTextChapters());
        }

        texts = sent;
        leftOut = [.. version.Chapters.Select(chapter => chapter.Id).Where(leftOut.Contains)];
        truncated = [.. version.Chapters.Select(chapter => chapter.Id).Where(truncated.Contains)];

        var share = requested / texts.Count;
        var remainder = requested % texts.Count;
        List<TestDraftChapter> chapters = [];
        for (var index = 0; index < texts.Count; index++)
        {
            var (id, title, source, text) = texts[index];
            var quota = QuestionCount.From(share + (index < remainder ? 1 : 0));
            chapters.Add(new TestDraftChapter(index + 1, id, title, source, text, quota));
        }

        return Success(new TestDraftRequest(chapters, QuestionCount.From(requested), leftOut, truncated));
    }

    /// <summary>
    /// How many characters of its text each PDF chapter may send out of <paramref name="budget"/>: shortest first, a PDF
    /// within an even share of what is left keeps its whole text, and the rest split what remains evenly.
    /// </summary>
    private static Dictionary<ChapterId, int> PdfAllowances(
        IReadOnlyList<(ChapterId Id, ChapterTitle Title, TestDraftSource Source, string Text)> texts,
        long budget)
    {
        var pdfs = texts.Where(chapter => chapter.Source == TestDraftSource.Pdf).OrderBy(chapter => chapter.Text.Length).ToList();
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
}
