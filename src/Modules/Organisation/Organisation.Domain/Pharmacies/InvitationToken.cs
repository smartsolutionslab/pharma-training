using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// The secret in an invitation link. Created by the Application from a cryptographic random source.
/// <see cref="From"/> guards a value the code already trusts; <see cref="TryFrom"/> checks a caller's, exactly as given.
/// </summary>
public sealed record InvitationToken : IValueObject<string>
{
    public const int MinLength = 32;

    public const int MaxLength = 128;

    private InvitationToken(string value) => Value = value;

    public string Value { get; }

    public static InvitationToken From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMinLength(MinLength).HasMaxLength(MaxLength);
        return new InvitationToken(value);
    }

    /// <summary>A caller's token, never trimmed: a token is exact, and one with other characters is another token.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out InvitationToken? token)
    {
        token = string.IsNullOrWhiteSpace(value) || value.Length < MinLength || value.Length > MaxLength
            ? null
            : new InvitationToken(value);
        return token is not null;
    }

    /// <summary>Never the secret itself, so no log or record print shows it; read <see cref="Value"/> for the link.</summary>
    public override string ToString() => "InvitationToken { *** }";
}
