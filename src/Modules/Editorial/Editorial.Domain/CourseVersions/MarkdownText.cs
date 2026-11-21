using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// The body of a text chapter, written in Markdown and kept exactly as written: never trimmed, never rendered here.
/// </summary>
public sealed record MarkdownText : IValueObject<string>
{
    public const int MaxLength = 200_000;

    private MarkdownText(string value) => Value = value;

    public string Value { get; }

    public static MarkdownText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "A nul character cannot be stored.");
        return new MarkdownText(value);
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out MarkdownText? text)
    {
        text = string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Contains('\0', StringComparison.Ordinal) ? null : new MarkdownText(value);
        return text is not null;
    }

    public override string ToString() => Value;
}
