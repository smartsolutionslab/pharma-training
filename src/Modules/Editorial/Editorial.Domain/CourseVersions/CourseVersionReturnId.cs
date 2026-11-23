namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>The identity of one return of a course version. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record CourseVersionReturnId : IValueObject<Guid>
{
    private CourseVersionReturnId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourseVersionReturnId New() => new(Guid.CreateVersion7());

    public static CourseVersionReturnId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CourseVersionReturnId(value);
    }

    public override string ToString() => Value.ToString();
}
