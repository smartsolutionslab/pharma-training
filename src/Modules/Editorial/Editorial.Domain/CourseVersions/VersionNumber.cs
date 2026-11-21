namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>Which version of its course a version is, counted from 1.</summary>
public sealed record VersionNumber : IValueObject<int>
{
    private VersionNumber(int value) => Value = value;

    public int Value { get; }

    public static VersionNumber First => new(1);

    public static VersionNumber From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new VersionNumber(value);
    }

    public VersionNumber Next() => new(Value + 1);
}
