namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A chapter's identity within its course version. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record ChapterId : IValueObject<Guid>
{
    private ChapterId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ChapterId New() => new(Guid.CreateVersion7());

    public static ChapterId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new ChapterId(value);
    }

    public override string ToString() => Value.ToString();
}
