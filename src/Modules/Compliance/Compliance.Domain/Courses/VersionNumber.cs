using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>A course version's number within its course, from one up; a higher number is a newer version.</summary>
public sealed record VersionNumber : IValueObject<int>, IComparable<VersionNumber>
{
    private VersionNumber(int value) => Value = value;

    public int Value { get; }

    public static VersionNumber From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new VersionNumber(value);
    }

    public static bool operator <(VersionNumber? left, VersionNumber? right) => Compare(left, right) < 0;

    public static bool operator <=(VersionNumber? left, VersionNumber? right) => Compare(left, right) <= 0;

    public static bool operator >(VersionNumber? left, VersionNumber? right) => Compare(left, right) > 0;

    public static bool operator >=(VersionNumber? left, VersionNumber? right) => Compare(left, right) >= 0;

    /// <summary>Orders by number; no number at all comes before every number.</summary>
    public int CompareTo(VersionNumber? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private static int Compare(VersionNumber? left, VersionNumber? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
