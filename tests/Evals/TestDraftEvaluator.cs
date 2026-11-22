using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// The AI test draft's own metrics, from 1 to 5 and without a judge, scored from the <see cref="TestDraftTranscript"/>
/// the use case attaches: every question refers to a chapter that was sent, the count matches the blueprint less the
/// approved questions, a single-choice question has one correct option, the questions follow each chapter's quota, and
/// the module's checks accepted the answer. A response without a transcript, another use case's, gets none of them.
/// </summary>
public sealed class TestDraftEvaluator : IEvaluator
{
    public const string ChapterReferencedMetricName = "Chapter referenced";

    public const string CountMatchesMetricName = "Count matches blueprint";

    public const string SingleChoiceOneCorrectMetricName = "Single choice has one correct";

    public const string EvenlyDistributedMetricName = "Evenly distributed";

    public const string AcceptedMetricName = "Accepted";

    private const double Lowest = 1;

    private const double Highest = 5;

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } =
        [ChapterReferencedMetricName, CountMatchesMetricName, SingleChoiceOneCorrectMetricName, EvenlyDistributedMetricName, AcceptedMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        Ensure.That(modelResponse).IsNotNull();

        if (modelResponse.AdditionalProperties?.TryGetValue(TestDraftTranscript.PropertyName, out var value) != true
            || value is not string json
            || TestDraftTranscript.FromJson(json) is not { } transcript)
        {
            return ValueTask.FromResult(new EvaluationResult());
        }

        return ValueTask.FromResult(new EvaluationResult(
            ChapterReferenced(transcript),
            CountMatches(transcript),
            SingleChoiceOneCorrect(transcript),
            EvenlyDistributed(transcript),
            Accepted(transcript)));
    }

    private static NumericMetric ChapterReferenced(TestDraftTranscript transcript)
    {
        if (transcript.Answer is not { Questions.Count: > 0 } answer)
        {
            return new NumericMetric(ChapterReferencedMetricName, Lowest, "No readable question.");
        }

        var referenced = answer.Questions.Count(question => question.Chapter >= 1 && question.Chapter <= transcript.Quotas.Count);
        return new NumericMetric(ChapterReferencedMetricName, Share(referenced, answer.Questions.Count), $"{referenced} of {answer.Questions.Count} questions name a chapter that was sent.");
    }

    private static NumericMetric CountMatches(TestDraftTranscript transcript)
    {
        var count = transcript.Answer?.Questions.Count;
        var matches = count == transcript.Requested;
        return new NumericMetric(CountMatchesMetricName, matches ? Highest : Lowest, $"{count?.ToString(CultureInfo.InvariantCulture) ?? "No readable"} questions for {transcript.Requested} requested.");
    }

    private static NumericMetric SingleChoiceOneCorrect(TestDraftTranscript transcript)
    {
        if (transcript.Answer is not { } answer)
        {
            return new NumericMetric(SingleChoiceOneCorrectMetricName, Lowest, "No readable answer.");
        }

        var singleChoice = answer.Questions.Where(question => question.Type == TestDraftAnswerType.SingleChoice).ToList();
        if (singleChoice.Count == 0)
        {
            return new NumericMetric(SingleChoiceOneCorrectMetricName, Highest, "No single-choice question.");
        }

        var oneCorrect = singleChoice.Count(question => question.Options.Count(option => option.Correct) == 1);
        return new NumericMetric(SingleChoiceOneCorrectMetricName, Share(oneCorrect, singleChoice.Count), $"{oneCorrect} of {singleChoice.Count} single-choice questions have exactly one correct option.");
    }

    private static NumericMetric EvenlyDistributed(TestDraftTranscript transcript)
    {
        if (transcript.Answer is not { } answer || transcript.Quotas.Count == 0)
        {
            return new NumericMetric(EvenlyDistributedMetricName, Lowest, "No readable answer.");
        }

        var onQuota = transcript.Quotas.Where((quota, index) => answer.Questions.Count(question => question.Chapter == index + 1) == quota).Count();
        return new NumericMetric(EvenlyDistributedMetricName, Share(onQuota, transcript.Quotas.Count), $"{onQuota} of {transcript.Quotas.Count} chapters have the number of questions asked for.");
    }

    private static NumericMetric Accepted(TestDraftTranscript transcript)
    {
        var accepted = transcript.Outcome == TestDraftTranscript.AcceptedOutcome;
        return new NumericMetric(AcceptedMetricName, accepted ? Highest : Lowest, accepted ? "The module's checks accepted the draft." : $"The module's checks rejected the draft: {transcript.Outcome}.");
    }

    private static double Share(int part, int whole) => Lowest + ((Highest - Lowest) * part / whole);
}
