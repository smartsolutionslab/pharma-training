using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// The AI content review as the evals run it: the example's course version, the module's request and prompt, the
/// model's answer read by the module's reviewer, and the module's checks, the composition of
/// <see cref="ContentReviewer.ReviewAsync"/> split so the raw answer is kept for scoring. The response's text renders
/// the answer for the judged metrics; its <see cref="ContentReviewTranscript"/> is what
/// <see cref="ContentReviewEvaluator"/> scores.
/// </summary>
public static class ContentReviewUseCase
{
    public const string Name = "ai-content-review";

    public static async Task<ChatResponse> AnswerAsync(IChatClient model, EvalExample example, CancellationToken cancellationToken)
    {
        Ensure.That(model).IsNotNull();
        Ensure.That(example).IsNotNull();

        var version = ContentReviewExamples.Version(example);
        var request = ContentReviewRequest.For(version);
        if (request.IsFailure)
        {
            throw new InvalidOperationException($"The example '{example.Input}' cannot be reviewed: {request.Error.Code}. Fix the example.");
        }

        var asked = await ContentReviewer.AskAsync(model, request.Value, cancellationToken);
        ContentReviewAnswer? answer = null;
        string outcome;
        if (asked.IsFailure)
        {
            outcome = Outcome(asked.Error);
        }
        else
        {
            answer = asked.Value;
            var review = answer.ToReview(request.Value);
            outcome = review.IsSuccess ? ContentReviewTranscript.AcceptedOutcome : Outcome(review.Error);
        }

        var transcript = new ContentReviewTranscript(request.Value.Chapters.Count, ContentReviewExamples.Planted(example), answer, outcome);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, Render(answer, outcome)))
        {
            AdditionalProperties = new() { [ContentReviewTranscript.PropertyName] = transcript.ToJson() },
        };
    }

    private static string Outcome(ApiError error) =>
        error is AiReviewMalformed malformed ? malformed.Defect.ToString() : error.Code;

    /// <summary>"Kapitel 2 (Lücke): …" per hint, or why nothing could be read.</summary>
    private static string Render(ContentReviewAnswer? answer, string outcome)
    {
        if (answer is null)
        {
            return $"No readable review: {outcome}";
        }

        var text = new StringBuilder();
        foreach (var hint in answer.Hints)
        {
            text.Append(CultureInfo.InvariantCulture, $"Kapitel {hint.Chapter} ({Kind(hint.Kind)}): {hint.Text}\n");
        }

        return text.Length == 0 ? "No hints." : text.ToString();
    }

    private static string Kind(ContentReviewAnswerKind kind) => kind switch
    {
        ContentReviewAnswerKind.Gap => "Lücke",
        ContentReviewAnswerKind.Contradiction => "Widerspruch",
        ContentReviewAnswerKind.Readability => "Lesbarkeit",
        _ => kind.ToString(),
    };
}
