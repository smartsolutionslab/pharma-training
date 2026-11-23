namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Everything the Pharmacy aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class PharmacyErrors
{
    public static DomainError AlreadyMemberOrInvited(PharmacyId pharmacy, EmailAddress email) => new AlreadyMemberOrInvited(pharmacy, email);

    public static DomainError NoOpenInvitation(PharmacyId pharmacy, MembershipId membership) => new NoOpenInvitation(pharmacy, membership);

    /// <summary>Issuing a link for an invitation that has run out; accepting one is <see cref="AcceptedAfterExpiry"/>.</summary>
    public static DomainError InvitationExpired(PharmacyId pharmacy, MembershipId membership) => new InvitationExpired(pharmacy, membership);

    /// <summary>Issuing a link for an invitation version that holds <see cref="Pharmacy.MaxInvitationLinksPerVersion"/> already.</summary>
    public static DomainError InvitationLinkLimitReached(PharmacyId pharmacy, MembershipId membership) => new InvitationLinkLimitReached(pharmacy, membership);

    public static AcceptInvitationError InvitationNotFound(PharmacyId pharmacy) => new InvitationNotFound(pharmacy);

    /// <summary>Accepting an invitation that has run out: the same <see cref="Pharmacies.InvitationExpired"/>, as one of accept's refusals.</summary>
    public static AcceptInvitationError AcceptedAfterExpiry(PharmacyId pharmacy, MembershipId membership) => new InvitationExpired(pharmacy, membership);

    public static AcceptInvitationError InvitationForAnotherAddress(PharmacyId pharmacy, MembershipId membership) => new InvitationForAnotherAddress(pharmacy, membership);

    public static AcceptInvitationError PersonAlreadyMember(PharmacyId pharmacy, PersonId person) => new PersonAlreadyMember(pharmacy, person);

    public static DomainError NoActiveMembership(PharmacyId pharmacy, MembershipId membership) => new NoActiveMembership(pharmacy, membership);

    public static DomainError LastManagerCannotLeave(PharmacyId pharmacy) => new LastManagerCannotLeave(pharmacy);
}
