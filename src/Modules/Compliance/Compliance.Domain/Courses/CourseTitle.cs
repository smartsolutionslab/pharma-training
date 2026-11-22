namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>
/// A course version's title, as Editorial sent it. Kept as it was sent, untrimmed; never blank, never longer than
/// Editorial allows a course title to be, and never holding a NUL character, which PostgreSQL refuses.
/// </summary>
public sealed record CourseTitle : IValueObject<string>
{
    /// <summary>Editorial's own maximum for a course title, which it trims before it sends one.</summary>
    public const int MaxLength = 150;

    private CourseTitle(string value) => Value = value;

    public string Value { get; }

    public static CourseTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "must not contain a NUL character.");
        return new CourseTitle(value);
    }

    public override string ToString() => Value;
}
