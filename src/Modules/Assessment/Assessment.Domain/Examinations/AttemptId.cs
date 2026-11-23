namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>One attempt at an examination's test. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record AttemptId : IValueObject<Guid>
{
    private AttemptId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AttemptId New() => new(Guid.CreateVersion7());

    public static AttemptId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new AttemptId(value);
    }

    public override string ToString() => Value.ToString();
}
