namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record NoOpenInvitation(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.no-open-invitation", "Es gibt keine offene Einladung.");
