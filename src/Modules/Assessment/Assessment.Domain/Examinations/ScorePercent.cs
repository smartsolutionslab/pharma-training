namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>An attempt's score, a whole percentage from 0 to 100. A score of right answers is rounded down: no partial credit.</summary>
public sealed record ScorePercent : IValueObject<int>, IComparable<ScorePercent>
{
    private ScorePercent(int value) => Value = value;

    public int Value { get; }

    public static ScorePercent From(int value)
    {
        Ensure.That(value).InRange(0, 100);
        return new ScorePercent(value);
    }

    /// <summary>The percentage of <paramref name="correct"/> among <paramref name="total"/> questions, rounded down.</summary>
    public static ScorePercent Of(QuestionCount correct, QuestionCount total)
    {
        Ensure.That(correct).IsNotNull();
        Ensure.That(total).IsNotNull();
        Ensure.That(total.Value).AtLeast(1);
        Ensure.That(correct.Value).InRange(0, total.Value);
        return new ScorePercent((int)Math.Floor(100m * correct.Value / total.Value));
    }

    public int CompareTo(ScorePercent? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator >=(ScorePercent left, ScorePercent right) => Compare(left, right) >= 0;

    public static bool operator <=(ScorePercent left, ScorePercent right) => Compare(left, right) <= 0;

    public static bool operator >(ScorePercent left, ScorePercent right) => Compare(left, right) > 0;

    public static bool operator <(ScorePercent left, ScorePercent right) => Compare(left, right) < 0;

    private static int Compare(ScorePercent? left, ScorePercent? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
