namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationNotFound(PharmacyId Pharmacy)
    : DomainError("organisation.invitation-not-found", "Der Einladungslink ist ungültig oder wurde schon benutzt.");
