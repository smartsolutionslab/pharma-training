namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

/// <summary>
/// A membership's identity, as Organisation minted it. An empty Guid is never one. Copied from Compliance's
/// <c>Compliance.Domain/Members/MembershipId.cs</c>.
/// </summary>
public sealed record MembershipId : IValueObject<Guid>
{
    private MembershipId(Guid value) => Value = value;

    public Guid Value { get; }

    public static MembershipId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new MembershipId(value);
    }

    public override string ToString() => Value.ToString();
}
