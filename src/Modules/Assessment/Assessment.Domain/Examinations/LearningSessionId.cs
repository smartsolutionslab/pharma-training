namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>The Learning session an examination belongs to, as Learning published it. An empty Guid is never one.</summary>
public sealed record LearningSessionId : IValueObject<Guid>
{
    private LearningSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static LearningSessionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new LearningSessionId(value);
    }

    public override string ToString() => Value.ToString();
}
