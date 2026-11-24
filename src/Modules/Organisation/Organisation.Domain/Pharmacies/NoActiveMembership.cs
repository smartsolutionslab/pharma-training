namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record NoActiveMembership(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.no-active-membership", "This pharmacy has no such active membership.");
