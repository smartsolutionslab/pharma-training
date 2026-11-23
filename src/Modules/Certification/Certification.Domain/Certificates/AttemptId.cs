namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>A test attempt's identity, as Assessment minted it. An empty Guid is never one.</summary>
public sealed record AttemptId : IValueObject<Guid>
{
    private AttemptId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AttemptId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new AttemptId(value);
    }

    public override string ToString() => Value.ToString();
}
