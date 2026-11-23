namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>How far a learner got in one chapter: watched share of a video, scrolled share of a text, pages seen of a PDF.</summary>
public sealed record ProgressPercent : IValueObject<int>, IComparable<ProgressPercent>
{
    private ProgressPercent(int value) => Value = value;

    public int Value { get; }

    public static ProgressPercent None => new(0);

    public static ProgressPercent Complete => new(100);

    public static ProgressPercent From(int value)
    {
        Ensure.That(value).InRange(0, 100);
        return new ProgressPercent(value);
    }

    public int CompareTo(ProgressPercent? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator >(ProgressPercent left, ProgressPercent right) => Compare(left, right) > 0;

    public static bool operator <(ProgressPercent left, ProgressPercent right) => Compare(left, right) < 0;

    public static bool operator >=(ProgressPercent left, ProgressPercent right) => Compare(left, right) >= 0;

    public static bool operator <=(ProgressPercent left, ProgressPercent right) => Compare(left, right) <= 0;

    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} %";

    private static int Compare(ProgressPercent? left, ProgressPercent? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
