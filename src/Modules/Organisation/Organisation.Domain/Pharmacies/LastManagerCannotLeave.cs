namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record LastManagerCannotLeave(PharmacyId Pharmacy)
    : DomainError("organisation.last-manager", "Die letzte Apothekenleitung kann nicht entfernt oder herabgestuft werden.");
