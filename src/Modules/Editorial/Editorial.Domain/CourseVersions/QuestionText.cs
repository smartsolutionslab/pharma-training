namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>What a test question asks, 1 to <see cref="MaxLength"/> characters and not blank, trimmed.</summary>
public sealed record QuestionText : IValueObject<string>
{
    public const int MaxLength = 1000;

    private QuestionText(string value) => Value = value;

    public string Value { get; }

    public static QuestionText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new QuestionText(value.Trim());
    }
}
