using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// Which state of a membership this is, as Organisation counts it: 1 at its start, the next at each change of job
/// function or role. A higher version is a newer state.
/// </summary>
public sealed record MembershipVersion : IValueObject<int>, IComparable<MembershipVersion>
{
    private MembershipVersion(int value) => Value = value;

    public int Value { get; }

    /// <summary>The state a membership starts in; a stored <see cref="Membership"/> is at this version.</summary>
    public static MembershipVersion First => new(1);

    public static MembershipVersion From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new MembershipVersion(value);
    }

    public static bool operator <(MembershipVersion? left, MembershipVersion? right) => Compare(left, right) < 0;

    public static bool operator <=(MembershipVersion? left, MembershipVersion? right) => Compare(left, right) <= 0;

    public static bool operator >(MembershipVersion? left, MembershipVersion? right) => Compare(left, right) > 0;

    public static bool operator >=(MembershipVersion? left, MembershipVersion? right) => Compare(left, right) >= 0;

    /// <summary>Orders by number; no version at all comes before every version.</summary>
    public int CompareTo(MembershipVersion? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private static int Compare(MembershipVersion? left, MembershipVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
