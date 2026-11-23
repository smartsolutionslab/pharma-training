using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

/// <summary>
/// Which state of a membership this is, as Organisation counts it: 1 at its start, the next at each change of job
/// function or role. Learning keeps it only as the second half of a change's key, so a repeated change is stored once.
/// Copied from Compliance's <c>Compliance.Domain/Members/MembershipVersion.cs</c>, without the ordering Learning never uses.
/// </summary>
public sealed record MembershipVersion : IValueObject<int>
{
    private MembershipVersion(int value) => Value = value;

    public int Value { get; }

    public static MembershipVersion From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new MembershipVersion(value);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
