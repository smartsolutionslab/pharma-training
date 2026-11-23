namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>A chapter of a course version, as Editorial published it. An empty Guid is never one.</summary>
public sealed record ChapterId : IValueObject<Guid>
{
    private ChapterId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ChapterId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new ChapterId(value);
    }

    public override string ToString() => Value.ToString();
}
