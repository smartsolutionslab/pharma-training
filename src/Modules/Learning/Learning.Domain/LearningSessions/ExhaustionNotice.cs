namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>The identifier of the AttemptsExhausted announcement that locked a session's test. An empty Guid is never one.</summary>
public sealed record ExhaustionNotice : IValueObject<Guid>
{
    private ExhaustionNotice(Guid value) => Value = value;

    public Guid Value { get; }

    public static ExhaustionNotice From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new ExhaustionNotice(value);
    }

    public override string ToString() => Value.ToString();
}
