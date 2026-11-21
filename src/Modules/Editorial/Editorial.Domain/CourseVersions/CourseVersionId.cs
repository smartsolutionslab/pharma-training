namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A course version's identity. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record CourseVersionId : IValueObject<Guid>
{
    private CourseVersionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourseVersionId New() => new(Guid.CreateVersion7());

    public static CourseVersionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CourseVersionId(value);
    }

    public override string ToString() => Value.ToString();
}
