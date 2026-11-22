using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// What the AI content review sends, worked out from the version before the model is called (ADR 0009): its text
/// chapters, numbered from one in the version's order, whatever state the version is in.
/// </summary>
public sealed record ContentReviewRequest(IReadOnlyList<ContentReviewChapter> Chapters)
{
    /// <summary>The most characters of chapter titles and texts one request sends, about 100k tokens.</summary>
    public const int MaxCharacters = 400_000;

    public static Result<ContentReviewRequest, ApiError> For(CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        List<ContentReviewChapter> chapters = [];
        foreach (var chapter in version.Chapters)
        {
            if (chapter is { Kind: ChapterKind.Text, Text: { } text })
            {
                chapters.Add(new ContentReviewChapter(chapters.Count + 1, chapter.Id, chapter.Title, text));
            }
        }

        if (chapters.Count == 0)
        {
            return Failure(ContentReviewFailures.NoTextChapters());
        }

        var characters = chapters.Sum(chapter => (long)chapter.Title.Value.Length + chapter.Text.Value.Length);
        if (characters > MaxCharacters)
        {
            return Failure(ContentReviewFailures.TooMuchContent());
        }

        return Success(new ContentReviewRequest(chapters));
    }
}
