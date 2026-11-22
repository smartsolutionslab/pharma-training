using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

/// <summary>
/// Reads the caller from the access token's claims, whether the handler kept the token's own names (<c>sub</c>,
/// <c>name</c>, <c>email</c>) or mapped them to <see cref="ClaimTypes"/>. A value that is missing or not what it
/// should be leaves no caller: the token is the only source of who someone is.
/// </summary>
public static class CallerClaims
{
    /// <summary>The person, from the subject alone; a subject that is no Guid, or the empty one, is no person.</summary>
    public static Option<PersonId> PersonOf(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        if (Guid.TryParse(First(user, "sub", ClaimTypes.NameIdentifier), out var subject) && subject != Guid.Empty)
        {
            return Option<PersonId>.Some(PersonId.From(subject));
        }

        return Option<PersonId>.None;
    }

    /// <summary>The person with their name (or, without one that fits, their user name) and a valid e-mail address.</summary>
    public static Option<Caller> Of(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        var person = PersonOf(user);
        if (person.HasValue
            && (DisplayName.TryFrom(First(user, "name", ClaimTypes.Name), out var name) || DisplayName.TryFrom(First(user, "preferred_username"), out name))
            && EmailAddress.TryFrom(First(user, "email", ClaimTypes.Email), out var email))
        {
            return Option<Caller>.Some(new Caller(person.Value, name, email));
        }

        return Option<Caller>.None;
    }

    /// <summary>
    /// Whether Keycloak verified the account's address: only an <c>email_verified</c> claim that reads as
    /// <see langword="true"/>. A missing claim, or any other value, is unverified (fail closed).
    /// </summary>
    public static bool HasVerifiedEmail(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        return bool.TryParse(user.FindFirst("email_verified")?.Value, out var verified) && verified;
    }

    private static string? First(ClaimsPrincipal user, params string[] types) =>
        types.Select(type => user.FindFirst(type)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
