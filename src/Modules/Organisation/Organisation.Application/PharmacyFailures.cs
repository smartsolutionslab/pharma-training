using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Diagnostics;
using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
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

    /// <summary>An accept lost the race to another change of the invitation; it names no pharmacy, which the caller does not belong to.</summary>
    public static ApiError InvitationChanged() => new InvitationChanged();

    /// <summary>Another save came at the same moment and PostgreSQL ended this one; try again.</summary>
    public static ApiError ConcurrentSave() => new OrganisationConcurrentSave();

    /// <summary>How a save of the pharmacy lost a race.</summary>
    public static ApiError LostRace(LostRace race, Guid id) => race switch
    {
        VersionMovedOn => Changed(id),
        SaveDeadlocked => ConcurrentSave(),
    };

    /// <summary>How the save of an acceptance lost a race.</summary>
    public static ApiError InvitationLostRace(LostRace race) => race switch
    {
        VersionMovedOn => InvitationChanged(),
        SaveDeadlocked => ConcurrentSave(),
    };

    public static ApiError InvalidEmail() => new InvalidEmail();

    public static ApiError OpenInvitationNotFound(Guid id) => new OpenInvitationNotFound(id);

    /// <summary>No pharmacy holds the link; the same answer as a pharmacy's <see cref="InvitationNotFound"/>, so the two cannot be told apart.</summary>
    public static ApiError InvitationLinkNotFound() => new InvitationLinkNotFound();

    public static ApiError EmailNotVerified() => new EmailNotVerified();

    public static ApiError InvalidInvitationToken() => new InvalidInvitationToken();

    /// <summary>The per-user limit on accepting invitations refused the request (docs/adr/0004).</summary>
    public static ApiError AcceptLimitReached() => new AcceptLimitReached();

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
            InvitationExpired => ApiError.From(error, HttpStatusCode.Gone),
            InvitationRenewedTooSoon tooSoon => ApiError.RetryLater(tooSoon, tooSoon.RetryAt.Value, tooSoon.RefusedAt.Value),
            TooManyInvitationsToday tooMany => ApiError.RetryLater(tooMany, tooMany.RetryAt.Value, tooMany.RefusedAt.Value),
            RecipientMailedRecently recently => ApiError.RetryLater(recently, recently.RetryAt.Value, recently.RefusedAt.Value),
            TooManyInvitationsSentToday tooManySent => ApiError.RetryLater(tooManySent, tooManySent.RetryAt.Value, tooManySent.RefusedAt.Value),
            _ => throw new UnreachableException(
                $"{error.Code} is not returned by an Organisation use case yet; map it where it is first returned."),
        };
    }

    /// <summary>Accept's refusals, each case mapped; a new case fails the build until it is mapped here.</summary>
    public static ApiError From(AcceptInvitationError error) => error switch
    {
        InvitationNotFound => InvitationLinkNotFound(),
        InvitationExpired expired => ApiError.From(expired, HttpStatusCode.Gone),
        InvitationForAnotherAddress another => ApiError.From(another, HttpStatusCode.Forbidden),
        PersonAlreadyMember member => ApiError.From(member, HttpStatusCode.Conflict),
    };
}
