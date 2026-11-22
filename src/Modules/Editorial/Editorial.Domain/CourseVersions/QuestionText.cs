using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// What a test question asks, 1 to <see cref="MaxLength"/> characters, not blank and without a nul character, trimmed.
/// <see cref="From"/> guards a value the code already trusts; <see cref="TryFrom"/> checks a caller's.
/// </summary>
public sealed record QuestionText : IValueObject<string>
{
    public const int MaxLength = 1000;

    private QuestionText(string value) => Value = value;

    public string Value { get; }

    public static QuestionText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "A nul character cannot be stored.");
        return new QuestionText(value.Trim());
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would: the length counts before trimming.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out QuestionText? result)
    {
        result = string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Contains('\0', StringComparison.Ordinal) ? null : new QuestionText(value.Trim());
        return result is not null;
    }
}
