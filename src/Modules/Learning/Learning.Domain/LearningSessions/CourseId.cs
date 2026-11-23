namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>A course's identity, as Editorial published it. An empty Guid is never one.</summary>
public sealed record CourseId : IValueObject<Guid>
{
    private CourseId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourseId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CourseId(value);
    }

    public override string ToString() => Value.ToString();
}
