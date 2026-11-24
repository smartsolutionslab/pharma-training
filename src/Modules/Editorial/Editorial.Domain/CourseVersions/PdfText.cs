using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// The text extracted from a PDF chapter's pages when it was recorded (ADR 0008 D11): never blank, at most as long as
/// the longest text chapter, and without the nul character PostgreSQL's <c>text</c> cannot hold.
/// </summary>
public sealed record PdfText : IValueObject<string>
{
    public const int MaxLength = MarkdownText.MaxLength;

    private PdfText(string value) => Value = value;

    public string Value { get; }

    public static PdfText From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "A nul character cannot be stored.");
        return new PdfText(value);
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out PdfText? text)
    {
        text = string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Contains('\0', StringComparison.Ordinal) ? null : new PdfText(value);
        return text is not null;
    }

    public override string ToString() => Value;
}
