using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// What the AI test draft asks the model for, worked out from the version before the model is called (ADR 0008): its
/// chapters with text (a text chapter's Markdown, a PDF's extracted text), numbered together from one in the version's
/// order, and the blueprint's count less the approved questions, split evenly over them with the remainder on the first
/// ones. Video chapters and PDF chapters without text are left out, and the PDF chapters drafted from text cut at a
/// limit are named, each in the version's order, so the editor knows.
/// </summary>
public sealed record TestDraftRequest(
    IReadOnlyList<TestDraftChapter> Chapters,
    QuestionCount Questions,
    IReadOnlyList<ChapterId> LeftOutChapters,
    IReadOnlyList<ChapterId> TruncatedChapters)
{
    /// <summary>The most characters of chapter titles and texts one request sends, about 100k tokens.</summary>
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

        var approved = version.Questions.Count(question => question.Review == ReviewState.Approved);
        var requested = version.Blueprint.Questions.Value - approved;
        if (requested <= 0)
        {
            return Failure(CourseVersionFailures.NothingToDraft());
        }

        var characters = texts.Sum(chapter => (long)chapter.Title.Value.Length + chapter.Text.Length);
        if (characters > MaxCharacters)
        {
            return Failure(CourseVersionFailures.TooMuchContentToDraft());
        }

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
}
