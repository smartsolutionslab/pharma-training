namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

/// <summary>A learning session's identity, as Learning minted it. An empty Guid is never one.</summary>
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
