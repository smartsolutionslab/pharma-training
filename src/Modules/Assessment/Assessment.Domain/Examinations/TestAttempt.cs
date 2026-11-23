namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>One run through the test, an entity of its <see cref="Examination"/>. Questions are shuffled per attempt; the order is stored.</summary>
public sealed class TestAttempt
{
    private readonly Dictionary<QuestionVersionId, IReadOnlyList<AnswerOptionId>> answers = [];

    internal TestAttempt(AttemptId id, AttemptNumber number, IReadOnlyList<QuestionVersionId> questions)
    {
        Id = id;
        Number = number;
        Questions = questions;
    }

    public AttemptId Id { get; }

    public AttemptNumber Number { get; }

    public IReadOnlyList<QuestionVersionId> Questions { get; }

    public AttemptOutcome? Outcome { get; private set; }

    public ScorePercent? Score { get; private set; }

    /// <summary>The learner's last answer to each question answered so far.</summary>
    public IReadOnlyDictionary<QuestionVersionId, IReadOnlyList<AnswerOptionId>> Answers => answers;

    /// <summary>The chapters of the questions this attempt got wrong, once submitted. Stub for the red run of #46: not implemented yet.</summary>
    public IReadOnlyList<ChapterId> FailedChapters => throw new NotImplementedException();

    internal void Answer(QuestionVersionId question, IReadOnlyList<AnswerOptionId> chosen) => answers[question] = chosen;

    internal void Close(ScorePercent score, AttemptOutcome outcome)
    {
        Score = score;
        Outcome = outcome;
    }
}
