namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>An assignment's identity, minted by Compliance as a Guid v7. An empty Guid is never one.</summary>
public sealed record AssignmentId : IValueObject<Guid>
{
    private AssignmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AssignmentId New() => new(Guid.CreateVersion7());

    public static AssignmentId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new AssignmentId(value);
    }

    public override string ToString() => Value.ToString();
}
