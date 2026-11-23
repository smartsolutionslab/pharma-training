namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>The learner's identity, as Learning published it. An empty Guid is never one.</summary>
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
