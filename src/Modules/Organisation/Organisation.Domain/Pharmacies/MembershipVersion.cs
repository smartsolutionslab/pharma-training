namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Which state of a membership this is, counted from 1 at its start; each change of job function or role is the next.</summary>
public sealed record MembershipVersion : IValueObject<int>
{
    private MembershipVersion(int value) => Value = value;

    public int Value { get; }

    public static MembershipVersion First => new(1);

    public static MembershipVersion From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new MembershipVersion(value);
    }

    public MembershipVersion Next() => new(Value + 1);
}
