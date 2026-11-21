using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// A chapter's title, 1 to <see cref="MaxLength"/> characters and not blank, trimmed. <see cref="From"/> guards a value the code
/// already trusts; <see cref="TryFrom"/> checks a caller's, so a bad one is the caller's mistake rather than an exception.
/// </summary>
public sealed record ChapterTitle : IValueObject<string>
{
    public const int MaxLength = 150;

    private ChapterTitle(string value) => Value = value;

    public string Value { get; }

    public static ChapterTitle From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "A nul character cannot be stored.");
        return new ChapterTitle(value.Trim());
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would: the length counts before trimming.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out ChapterTitle? result)
    {
        result = string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Contains('\0', StringComparison.Ordinal) ? null : new ChapterTitle(value.Trim());
        return result is not null;
    }

    public override string ToString() => Value;
}
