namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A person's identity: the Keycloak subject of the person. An empty Guid is never one.</summary>
public sealed record PersonId : IValueObject<Guid>
{
    private PersonId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PersonId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new PersonId(value);
    }

    public override string ToString() => Value.ToString();
}
