namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationNotFound(PharmacyId Pharmacy)
    : DomainError("organisation.invitation-not-found", "The invitation link is invalid or has already been used.");
