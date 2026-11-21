namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Everything the Pharmacy aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class PharmacyErrors
{
    public static DomainError AlreadyMemberOrInvited(PharmacyId pharmacy, EmailAddress email) => new AlreadyMemberOrInvited(pharmacy, email);

    public static DomainError NoOpenInvitation(PharmacyId pharmacy, MembershipId membership) => new NoOpenInvitation(pharmacy, membership);

    public static DomainError InvitationNotFound(PharmacyId pharmacy) => new InvitationNotFound(pharmacy);

    public static DomainError InvitationExpired(PharmacyId pharmacy, MembershipId membership) => new InvitationExpired(pharmacy, membership);

    public static DomainError InvitationForAnotherAddress(PharmacyId pharmacy, MembershipId membership) => new InvitationForAnotherAddress(pharmacy, membership);

    public static DomainError PersonAlreadyMember(PharmacyId pharmacy, PersonId person) => new PersonAlreadyMember(pharmacy, person);

    public static DomainError NoActiveMembership(PharmacyId pharmacy, MembershipId membership) => new NoActiveMembership(pharmacy, membership);

    public static DomainError LastManagerCannotLeave(PharmacyId pharmacy) => new LastManagerCannotLeave(pharmacy);
}
