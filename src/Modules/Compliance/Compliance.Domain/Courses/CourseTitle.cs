namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>
/// A course version's title, as Editorial sent it. Kept as it was sent, untrimmed and without a length of Compliance's own; never blank and never holding a NUL
/// character, which PostgreSQL refuses.
/// </summary>
public sealed record CourseTitle : IValueObject<string>
{
    private CourseTitle(string value) => Value = value;

    public string Value { get; }

    public static CourseTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "must not contain a NUL character.");
        return new CourseTitle(value);
    }

    public override string ToString() => Value;
}
