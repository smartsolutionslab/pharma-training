using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// Reads the calling editor from the access token's subject, whether the handler kept its own name (<c>sub</c>) or
/// mapped it to <see cref="ClaimTypes.NameIdentifier"/>. A subject that is missing, no Guid or the empty one is no person.
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
}
