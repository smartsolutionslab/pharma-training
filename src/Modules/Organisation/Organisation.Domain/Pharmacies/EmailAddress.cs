using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// An e-mail address, stored trimmed and lower-case so that two spellings of one address are equal.
/// <see cref="From"/> guards a value the code already trusts; <see cref="TryFrom"/> checks a caller's.
/// </summary>
public sealed partial record EmailAddress : IValueObject<string>
{
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace();
        var trimmed = value.Trim();
        Ensure.That(trimmed).HasMaxLength(MaxLength).Matches(Shape(), "Keine gültige E-Mail-Adresse.");
        return new EmailAddress(trimmed.ToLowerInvariant());
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out EmailAddress? address)
    {
        var trimmed = value?.Trim();
        address = string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || !Shape().IsMatch(trimmed)
            ? null
            : new EmailAddress(trimmed.ToLowerInvariant());
        return address is not null;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Shape();
}
