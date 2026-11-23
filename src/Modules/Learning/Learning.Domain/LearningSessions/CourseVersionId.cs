namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>A published course version's identity, as Editorial published it. An empty Guid is never one.</summary>
public sealed record CourseVersionId : IValueObject<Guid>
{
    private CourseVersionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourseVersionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CourseVersionId(value);
    }

    public override string ToString() => Value.ToString();
}
