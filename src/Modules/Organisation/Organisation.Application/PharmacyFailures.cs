using System.Diagnostics;
using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Everything the pharmacies' use cases refuse, each with its own code.</summary>
public static class PharmacyFailures
{
    public static ApiError InvalidName() => new InvalidPharmacyName();

    public static ApiError InvalidJobFunction() => new InvalidJobFunction();

    public static ApiError InvalidMembershipRole() => new InvalidMembershipRole();

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    public static ApiError NotFound(Guid id) => new PharmacyNotFound(id);

    public static ApiError CapabilityMissing() => new CapabilityMissing();

    public static ApiError MembershipNotFound(Guid id) => new MembershipNotFound(id);

    public static ApiError Changed(Guid id) => new PharmacyChanged(id);

    public static ApiError InvalidEmail() => new InvalidEmail();

    public static ApiError OpenInvitationNotFound(Guid id) => new OpenInvitationNotFound(id);

    /// <summary>No pharmacy holds the link; the same answer as a pharmacy's <see cref="InvitationNotFound"/>, so the two cannot be told apart.</summary>
    public static ApiError InvitationLinkNotFound() => new InvitationLinkNotFound();

    public static ApiError EmailNotVerified() => new EmailNotVerified();

    public static ApiError InvalidInvitationToken() => new InvalidInvitationToken();

    /// <summary>
    /// The domain's refusals with the status each use case answers them with. An error no use case returns yet has no
    /// status here: it is mapped where it is first returned, never given a guessed one.
    /// </summary>
    public static ApiError From(DomainError error)
    {
        Ensure.That(error).IsNotNull();

        return error switch
        {
            NoActiveMembership => ApiError.From(error, HttpStatusCode.NotFound),
            LastManagerCannotLeave => ApiError.From(error, HttpStatusCode.Conflict),
            AlreadyMemberOrInvited => ApiError.From(error, HttpStatusCode.Conflict),
            NoOpenInvitation => ApiError.From(error, HttpStatusCode.NotFound),
            InvitationNotFound => InvitationLinkNotFound(),
            InvitationExpired => ApiError.From(error, HttpStatusCode.Gone),
            InvitationForAnotherAddress => ApiError.From(error, HttpStatusCode.Forbidden),
            PersonAlreadyMember => ApiError.From(error, HttpStatusCode.Conflict),
            _ => throw new UnreachableException(
                $"{error.Code} is not returned by an Organisation use case yet; map it where it is first returned."),
        };
    }
}
