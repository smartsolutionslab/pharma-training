namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>Which attempt since the examination was last unlocked this is: 1, 2 or 3.</summary>
public sealed record AttemptNumber : IValueObject<int>, IComparable<AttemptNumber>
{
    private AttemptNumber(int value) => Value = value;

    public int Value { get; }

    public static AttemptNumber First => new(1);

    public static AttemptNumber From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new AttemptNumber(value);
    }

    public AttemptNumber Next() => new(Value + 1);

    public int CompareTo(AttemptNumber? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator >=(AttemptNumber left, AttemptNumber right) => Compare(left, right) >= 0;

    public static bool operator <=(AttemptNumber left, AttemptNumber right) => Compare(left, right) <= 0;

    public static bool operator >(AttemptNumber left, AttemptNumber right) => Compare(left, right) > 0;

    public static bool operator <(AttemptNumber left, AttemptNumber right) => Compare(left, right) < 0;

    private static int Compare(AttemptNumber? left, AttemptNumber? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
