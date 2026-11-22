using System.Diagnostics;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>The model's answer to an AI content review, in the shape its JSON schema asks for: untrusted until checked.</summary>
public sealed record ContentReviewAnswer(IReadOnlyList<ContentReviewAnswerHint> Hints)
{
    /// <summary>The most hints one chapter may get, as the instruction asks.</summary>
    public const int MaxHintsPerChapter = 5;

    /// <summary>
    /// Checks the answer against the request and maps it onto the chapters sent (ADR 0009), in this order: chapter
    /// numbers that were sent, hint texts that can be shown once trimmed, and at most <see cref="MaxHintsPerChapter"/>
    /// hints per chapter. Any defect fails the whole answer; no hints and duplicate hints are accepted. The hints are
    /// ordered by chapter, keeping the model's order within one.
    /// </summary>
    public Result<ContentReview, ApiError> ToReview(ContentReviewRequest request)
    {
        Ensure.That(request).IsNotNull();

        if (Hints.Any(hint => hint.Chapter < 1 || hint.Chapter > request.Chapters.Count))
        {
            return Malformed(ContentReviewDefect.UnknownChapter);
        }

        if (Hints.Any(hint => !IsShowable(hint.Text.Trim())))
        {
            return Malformed(ContentReviewDefect.InvalidHintText);
        }

        if (Hints.GroupBy(hint => hint.Chapter).Any(chapter => chapter.Count() > MaxHintsPerChapter))
        {
            return Malformed(ContentReviewDefect.TooManyHints);
        }

        IReadOnlyList<ContentHint> hints =
        [
            .. Hints
                .OrderBy(hint => hint.Chapter)
                .Select(hint => new ContentHint(request.Chapters[hint.Chapter - 1].Id, Kind(hint.Kind), hint.Text.Trim())),
        ];

        return Success(new ContentReview([.. request.Chapters.Select(chapter => chapter.Id)], hints));
    }

    private static bool IsShowable(string text) =>
        text.Length > 0 && text.Length <= ContentHint.MaxTextLength && !text.Contains('\0', StringComparison.Ordinal);

    private static ContentHintKind Kind(ContentReviewAnswerKind kind) => kind switch
    {
        ContentReviewAnswerKind.Gap => ContentHintKind.Gap,
        ContentReviewAnswerKind.Contradiction => ContentHintKind.Contradiction,
        ContentReviewAnswerKind.Readability => ContentHintKind.Readability,
        _ => throw new UnreachableException($"Undefined hint kind {kind}."),
    };

    private static Result<ContentReview, ApiError> Malformed(ContentReviewDefect defect) =>
        Failure(ContentReviewFailures.Malformed(defect));
}
