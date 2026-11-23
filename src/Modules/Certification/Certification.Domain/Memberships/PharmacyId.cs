namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>A pharmacy's identity, as Organisation minted it. An empty Guid is never one.</summary>
public sealed record PharmacyId : IValueObject<Guid>
{
    private PharmacyId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PharmacyId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new PharmacyId(value);
    }

    public override string ToString() => Value.ToString();
}
