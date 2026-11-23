using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Why an editor returned a version to draft, trimmed: 1 to <see cref="MaxLength"/> characters after trimming, not blank,
/// and plain text that holds no control character but a line break or a tab and no format character (bidi overrides,
/// zero-width characters), line or paragraph separator, private-use or unassigned code point, or unpaired surrogate.
/// Markup is kept as written: it is the editor's own text, answered as a string. <see cref="From"/> guards a value the code already trusts;
/// <see cref="TryFrom"/> checks a caller's and succeeds exactly when <see cref="From"/> would.
/// </summary>
public sealed record ReturnReason : IValueObject<string>
{
    public const int MaxLength = 1000;

    public const string Message =
        "A return's reason is 1 to 1000 characters after trimming, and holds no control or format characters but line breaks and tabs.";

    private ReturnReason(string value) => Value = value;

    public string Value { get; }

    public static ReturnReason From(string value)
    {
        Ensure.That(value).IsNotNull().Satisfies(IsValid, Message);
        return new ReturnReason(value.Trim());
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out ReturnReason? result)
    {
        result = value is not null && IsValid(value) ? new ReturnReason(value.Trim()) : null;
        return result is not null;
    }

    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        var text = value.Trim();
        return text.Length is > 0 and <= MaxLength && PlainText.IsPlain(text, allowLineBreaksAndTabs: true);
    }
}
