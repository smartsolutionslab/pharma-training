using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// The display name of the editor who approved a question, as the access token's <c>name</c> claim carried it at approval
/// time (#205): a historical record, not changed when the account's name changes. <see cref="From"/> guards a value the
/// code already trusts; <see cref="TryFrom"/> checks a token's, so a bad one is no name rather than an exception. Both trim it first.
/// </summary>
public sealed record ReviewerName : IValueObject<string>
{
    public const int MaxLength = 100;

    private ReviewerName(string value) => Value = value;

    public string Value { get; }

    public static ReviewerName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace();
        var trimmed = value.Trim();
        Ensure.That(trimmed).HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "A nul character cannot be stored.");
        return new ReviewerName(trimmed);
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out ReviewerName? name)
    {
        var trimmed = value?.Trim();
        name = string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || trimmed.Contains('\0', StringComparison.Ordinal) ? null : new ReviewerName(trimmed);
        return name is not null;
    }

    public override string ToString() => Value;
}
