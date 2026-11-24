namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record LastManagerCannotLeave(PharmacyId Pharmacy)
    : DomainError("organisation.last-manager", "The last manager of a pharmacy cannot be removed or demoted.");
