using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// Reads the calling editor from the access token's subject, whether the handler kept its own name (<c>sub</c>) or
/// mapped it to <see cref="ClaimTypes.NameIdentifier"/>. A subject that is missing, no Guid or the empty one is no person.
/// The display name comes from <c>name</c>, or <see cref="ClaimTypes.Name"/>, never from the user name.
/// </summary>
public static class EditorialCaller
{
    public static Option<PersonId> PersonOf(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        var subject = new[] { "sub", ClaimTypes.NameIdentifier }
            .Select(type => user.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (Guid.TryParse(subject, out var person) && person != Guid.Empty)
        {
            return Option<PersonId>.Some(PersonId.From(person));
        }

        return Option<PersonId>.None;
    }

    /// <summary>
    /// The caller's display name, to record with an approval (#205) or a return (#220). A missing, blank or too long name, or one holding a
    /// character a reader cannot see or that turns the text around (see <see cref="ReviewerName"/>), is no name; there is
    /// no fallback to <c>preferred_username</c>.
    /// </summary>
    public static Option<ReviewerName> NameOf(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        var name = new[] { "name", ClaimTypes.Name }
            .Select(type => user.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (ReviewerName.TryFrom(name, out var reviewer))
        {
            return Option<ReviewerName>.Some(reviewer);
        }

        return Option<ReviewerName>.None;
    }
}
