namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>An answer option's text, 1 to <see cref="MaxLength"/> characters and not blank, trimmed.</summary>
public sealed record AnswerText : IValueObject<string>
{
    public const int MaxLength = 400;

    private AnswerText(string value) => Value = value;

    public string Value { get; }

    public static AnswerText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new AnswerText(value.Trim());
    }
}
