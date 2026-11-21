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
        Ensure.That(trimmed).HasMaxLength(MaxLength);
        return new PharmacyName(trimmed);
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out PharmacyName? name)
    {
        var trimmed = value?.Trim();
        name = string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength ? null : new PharmacyName(trimmed);
        return name is not null;
    }

    public override string ToString() => Value;
}
