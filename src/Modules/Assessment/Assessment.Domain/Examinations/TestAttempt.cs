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

    internal IReadOnlyDictionary<QuestionVersionId, IReadOnlyList<AnswerOptionId>> Answers => answers;

    internal void Answer(QuestionVersionId question, IReadOnlyList<AnswerOptionId> chosen) => answers[question] = chosen;

    internal void Close(ScorePercent score, AttemptOutcome outcome)
    {
        Score = score;
        Outcome = outcome;
    }
}
