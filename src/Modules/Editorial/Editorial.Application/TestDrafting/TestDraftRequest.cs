using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// What the AI test draft asks the model for, worked out from the version before the model is called (ADR 0008): its
/// text chapters, numbered from one in the version's order, and the blueprint's count less the questions the draft
/// keeps (every one but the unreviewed AI-drafted ones, #245), split evenly over them with the remainder on the first
/// ones. The PDF and video chapters are left out, in the version's order, and named so the editor knows.
/// </summary>
public sealed record TestDraftRequest(IReadOnlyList<TestDraftChapter> Chapters, QuestionCount Questions, IReadOnlyList<ChapterId> LeftOutChapters)
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

        List<(ChapterId Id, ChapterTitle Title, MarkdownText Text)> texts = [];
        List<ChapterId> leftOut = [];
        foreach (var chapter in version.Chapters)
        {
            if (chapter is { Kind: ChapterKind.Text, Text: { } text })
            {
                texts.Add((chapter.Id, chapter.Title, text));
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

        // Every question the draft keeps counts: approved ones, and copied or hand-written ones even while unreviewed (#245).
        var kept = version.Questions.Count(question => !question.IsReplacedByAiDraft());
        var requested = version.Blueprint.Questions.Value - kept;
        if (requested <= 0)
        {
            return Failure(CourseVersionFailures.NothingToDraft());
        }

        var characters = texts.Sum(chapter => (long)chapter.Title.Value.Length + chapter.Text.Value.Length);
        if (characters > MaxCharacters)
        {
            return Failure(CourseVersionFailures.TooMuchContentToDraft());
        }

        var share = requested / texts.Count;
        var remainder = requested % texts.Count;
        List<TestDraftChapter> chapters = [];
        for (var index = 0; index < texts.Count; index++)
        {
            var (id, title, text) = texts[index];
            var quota = QuestionCount.From(share + (index < remainder ? 1 : 0));
            chapters.Add(new TestDraftChapter(index + 1, id, title, text, quota));
        }

        return Success(new TestDraftRequest(chapters, QuestionCount.From(requested), leftOut));
    }
}
