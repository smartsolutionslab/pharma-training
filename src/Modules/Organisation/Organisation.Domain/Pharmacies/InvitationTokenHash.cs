using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// The SHA-256 of an invitation token in lower-case hex, the only form in which the token is stored. No salt and no
/// slow hash: the token is 256 random bits, not a password, and a link is found by its hash.
/// </summary>
public sealed partial record InvitationTokenHash : IValueObject<string>
{
    public const int Length = 64;

    private InvitationTokenHash(string value) => Value = value;

    public string Value { get; }

    public static InvitationTokenHash From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().Matches(Shape(), "Kein SHA-256 in Kleinbuchstaben-Hex.");
        return new InvitationTokenHash(value);
    }

    public static InvitationTokenHash Of(InvitationToken token)
    {
        Ensure.That(token).IsNotNull();
        return new InvitationTokenHash(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value))));
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Shape();
}
