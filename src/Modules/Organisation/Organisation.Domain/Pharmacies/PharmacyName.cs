using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// A pharmacy's name, 1 to <see cref="MaxLength"/> characters and not blank. <see cref="From"/> guards a value the code already trusts; <see cref="TryFrom"/> checks a caller's, so a bad
/// one is the caller's mistake rather than an exception. Both trim it first.
/// </summary>
public sealed record PharmacyName : IValueObject<string>
{
    public const int MaxLength = 120;

    private PharmacyName(string value) => Value = value;

    public string Value { get; }

    public static PharmacyName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace();
        var trimmed = value.Trim();
        Ensure.That(trimmed).HasMaxLength(MaxLength).Satisfies(IsPrintable, "Ein Name enthält keine Steuer- oder Richtungszeichen.");
        return new PharmacyName(trimmed);
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out PharmacyName? name)
    {
        var trimmed = value?.Trim();
        name = string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || !IsPrintable(trimmed) ? null : new PharmacyName(trimmed);
        return name is not null;
    }

    public override string ToString() => Value;

    /// <summary>
    /// No control character (a line break would end a mail's subject, a nul cannot be stored) and no bidi embedding,
    /// override or isolate (U+202A–U+202E, U+2066–U+2069), which would make a mail or a screen show another name.
    /// </summary>
    private static bool IsPrintable(string text) =>
        !text.Any(character => char.IsControl(character) || character is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069');
}
