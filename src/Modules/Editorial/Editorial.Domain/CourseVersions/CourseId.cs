namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A course's identity: the versions of one course share it. A course has no aggregate of its own. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record CourseId : IValueObject<Guid>
{
    private CourseId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourseId New() => new(Guid.CreateVersion7());

    public static CourseId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CourseId(value);
    }

    public override string ToString() => Value.ToString();
}
