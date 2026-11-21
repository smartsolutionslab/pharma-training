namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record NoActiveMembership(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.no-active-membership", "Es gibt keine aktive Mitgliedschaft.");
