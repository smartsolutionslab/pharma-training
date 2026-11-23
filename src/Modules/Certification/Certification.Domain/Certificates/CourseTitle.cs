namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>The title of the course version a certificate is for, as Editorial published it.</summary>
public sealed record CourseTitle : IValueObject<string>
{
    /// <summary>Editorial's own maximum for a course title.</summary>
    public const int MaxLength = 150;

    private CourseTitle(string value) => Value = value;

    public string Value { get; }

    public static CourseTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new CourseTitle(value.Trim());
    }

    public override string ToString() => Value;
}
