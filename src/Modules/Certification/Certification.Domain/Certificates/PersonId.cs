namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>A person's identity, as Organisation minted it. An empty Guid is never one.</summary>
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
