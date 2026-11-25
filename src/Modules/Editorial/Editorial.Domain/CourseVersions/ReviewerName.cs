using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// The display name of the editor who approved a question (#205) or returned a course version to draft (#220), as the
/// access token's <c>name</c> claim carried it at that time: a historical record, not changed when the account's name changes. The name is the user's own (the realm
/// allows self-registration and lets users edit their name), so it is checked like any input: 1 to <see cref="MaxLength"/>
/// characters after trimming, on one line, and plain text that holds no control character (a line break and a tab
/// included), no format character (bidi overrides, zero-width characters) other than the zero-width joiners a script
/// writes names with (see <see cref="PlainText"/>), no line or paragraph separator, private-use or unassigned code point,
/// and no unpaired surrogate; and it shows: at least one letter and no Default_Ignorable code point but those joiners
/// (no Hangul filler, combining grapheme joiner or variation selector). <see cref="From"/> guards a value the code already trusts;
/// <see cref="TryFrom"/> checks a token's, so a bad one is no name rather than an exception. Both trim it first.
/// </summary>
public sealed record ReviewerName : IValueObject<string>
{
    public const int MaxLength = 100;

    public const string Message =
        "A reviewer's name is 1 to 100 characters after trimming, and holds no control or format characters.";

    private ReviewerName(string value) => Value = value;

    public string Value { get; }

    public static ReviewerName From(string value)
    {
        Ensure.That(value).IsNotNull().Satisfies(IsValid, Message);
        return new ReviewerName(value.Trim());
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out ReviewerName? name)
    {
        name = value is not null && IsValid(value) ? new ReviewerName(value.Trim()) : null;
        return name is not null;
    }

    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        var text = value.Trim();
        return text.Length is > 0 and <= MaxLength && PlainText.IsPlainName(text);
    }
}
