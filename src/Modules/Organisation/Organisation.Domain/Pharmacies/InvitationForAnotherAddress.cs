namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationForAnotherAddress(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-for-another-address", "The invitation is for another e-mail address.");
