using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The account's address as Keycloak reports it, with whether Keycloak verified it. Whoever dispatches an accept says
/// whether the address is verified; the use case decides what an unverified one may do.
/// </summary>
public sealed record AccountEmail : IValueObject<EmailAddress>
{
    private AccountEmail(EmailAddress value, bool isVerified)
    {
        Value = value;
        IsVerified = isVerified;
    }

    public EmailAddress Value { get; }

    /// <summary>Whether Keycloak verified the address; an unverified one proves nothing about who holds the account.</summary>
    public bool IsVerified { get; }

    public static AccountEmail From(EmailAddress address, bool verified)
    {
        Ensure.That(address).IsNotNull();
        return new AccountEmail(address, verified);
    }

    public override string ToString() => Value.ToString();
}
