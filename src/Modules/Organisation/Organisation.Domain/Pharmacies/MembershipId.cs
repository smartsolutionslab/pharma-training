namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A membership's identity. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record MembershipId : IValueObject<Guid>
{
    private MembershipId(Guid value) => Value = value;

    public Guid Value { get; }

    public static MembershipId New() => new(Guid.CreateVersion7());

    public static MembershipId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new MembershipId(value);
    }

    public override string ToString() => Value.ToString();
}
