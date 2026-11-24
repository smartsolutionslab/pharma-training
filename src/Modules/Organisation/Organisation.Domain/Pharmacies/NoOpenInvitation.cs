namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record NoOpenInvitation(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.no-open-invitation", "This pharmacy has no such open invitation.");
