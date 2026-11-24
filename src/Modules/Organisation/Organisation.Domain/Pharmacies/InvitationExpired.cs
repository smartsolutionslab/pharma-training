namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationExpired(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-expired", "The invitation has expired; ask for a new one.");
