using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// The AI content review's own metrics, from 1 to 5 and without a judge, scored from the
/// <see cref="ContentReviewTranscript"/> the use case attaches: every hint names a chapter that was sent, the example's
/// planted issues are found, and the module's checks accepted the answer. A response without a transcript, another use
/// case's, gets none of them.
/// </summary>
public sealed class ContentReviewEvaluator : IEvaluator
{
    public const string HintChapterExistsMetricName = "Hint chapter exists";

    public const string PlantedIssueFoundMetricName = "Planted issue found";

    /// <summary>Not "Accepted": the AI test draft's evaluator reports that name, and one run may not hold two evaluators for one metric.</summary>
    public const string AcceptedMetricName = "Review accepted";

    private const double Lowest = 1;

    private const double Highest = 5;

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [HintChapterExistsMetricName, PlantedIssueFoundMetricName, AcceptedMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        Ensure.That(modelResponse).IsNotNull();

        if (modelResponse.AdditionalProperties?.TryGetValue(ContentReviewTranscript.PropertyName, out var value) != true
            || value is not string json
            || ContentReviewTranscript.FromJson(json) is not { } transcript)
        {
            return ValueTask.FromResult(new EvaluationResult());
        }

        return ValueTask.FromResult(new EvaluationResult(HintChapterExists(transcript), PlantedIssueFound(transcript), Accepted(transcript)));
    }

    private static NumericMetric HintChapterExists(ContentReviewTranscript transcript)
    {
        if (transcript.Answer is not { } answer)
        {
            return new NumericMetric(HintChapterExistsMetricName, Lowest, "No readable review.");
        }

        if (answer.Hints.Count == 0)
        {
            return new NumericMetric(HintChapterExistsMetricName, Highest, "No hints.");
        }

        var existing = answer.Hints.Count(hint => hint.Chapter >= 1 && hint.Chapter <= transcript.Chapters);
        return new NumericMetric(HintChapterExistsMetricName, Share(existing, answer.Hints.Count), $"{existing} of {answer.Hints.Count} hints name a chapter that was sent.");
    }

    private static NumericMetric PlantedIssueFound(ContentReviewTranscript transcript)
    {
        if (transcript.Answer is not { } answer || transcript.Planted.Count == 0)
        {
            return new NumericMetric(PlantedIssueFoundMetricName, Lowest, "No readable review.");
        }

        var found = transcript.Planted.Count(issue => answer.Hints.Any(hint =>
            string.Equals(hint.Kind.ToString(), issue.Kind.ToString(), StringComparison.Ordinal) && issue.Chapters.Contains(hint.Chapter)));
        return new NumericMetric(PlantedIssueFoundMetricName, Share(found, transcript.Planted.Count), $"{found} of {transcript.Planted.Count} planted issues found.");
    }

    private static NumericMetric Accepted(ContentReviewTranscript transcript)
    {
        var accepted = transcript.Outcome == ContentReviewTranscript.AcceptedOutcome;
        return new NumericMetric(AcceptedMetricName, accepted ? Highest : Lowest, accepted ? "The module's checks accepted the review." : $"The module's checks rejected the review: {transcript.Outcome}.");
    }

    private static double Share(int part, int whole) => Lowest + ((Highest - Lowest) * part / whole);
}
