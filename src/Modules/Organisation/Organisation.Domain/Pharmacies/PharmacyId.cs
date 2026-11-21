namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A pharmacy's identity. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record PharmacyId : IValueObject<Guid>
{
    private PharmacyId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PharmacyId New() => new(Guid.CreateVersion7());

    public static PharmacyId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new PharmacyId(value);
    }

    public override string ToString() => Value.ToString();
}
