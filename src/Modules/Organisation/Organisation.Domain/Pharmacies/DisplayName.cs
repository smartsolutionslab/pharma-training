using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// The name a person goes by, as Keycloak has it. The person keeps it there; the manager never types it. <see cref="From"/> guards a value the code already trusts; <see cref="TryFrom"/> checks a caller's, so a bad
/// one is the caller's mistake rather than an exception. Both trim it first.
/// </summary>
public sealed record DisplayName : IValueObject<string>
{
    public const int MaxLength = 100;

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    public static DisplayName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace();
        var trimmed = value.Trim();
        Ensure.That(trimmed).HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "A nul character cannot be stored.");
        return new DisplayName(trimmed);
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out DisplayName? name)
    {
        var trimmed = value?.Trim();
        name = string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || trimmed.Contains('\0', StringComparison.Ordinal) ? null : new DisplayName(trimmed);
        return name is not null;
    }

    public override string ToString() => Value;
}
