namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record PersonAlreadyMember(PharmacyId Pharmacy, PersonId Person)
    : DomainError("organisation.person-already-member", "Diese Person ist bereits Mitglied der Apotheke.");
