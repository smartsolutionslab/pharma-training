namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>What the test of this version must look like; the AI draft is made to it and Publish checks against it.</summary>
public sealed record TestBlueprint
{
    public const int MinimumQuestions = 1;

    public TestBlueprint(QuestionCount questions, ScorePercent passingScore, RecurrenceInterval validity)
    {
        Ensure.That(questions).IsNotNull();
        Ensure.That(passingScore).IsNotNull();
        Ensure.That(validity).IsNotNull();
        Ensure.That(questions.Value).AtLeast(MinimumQuestions);
        Questions = questions;
        PassingScore = passingScore;
        Validity = validity;
    }

    public QuestionCount Questions { get; }

    public ScorePercent PassingScore { get; }

    /// <summary>How long a certificate for this version is valid, and how often a recurring assignment repeats.</summary>
    public RecurrenceInterval Validity { get; }

    /// <summary>The blueprint, or <see cref="TooFewQuestions"/> when it asks for fewer than <see cref="MinimumQuestions"/>.</summary>
    public static Result<TestBlueprint, DomainError> TryCreate(QuestionCount questions, ScorePercent passingScore, RecurrenceInterval validity)
    {
        Ensure.That(questions).IsNotNull();

        if (questions.Value < MinimumQuestions)
        {
            return Failure(CourseVersionErrors.TooFewQuestions(questions));
        }

        return Success(new TestBlueprint(questions, passingScore, validity));
    }

    public static TestBlueprint Default => new(QuestionCount.Default, ScorePercent.From(80), RecurrenceInterval.Yearly);
}
