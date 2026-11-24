namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record PersonAlreadyMember(PharmacyId Pharmacy, PersonId Person)
    : DomainError("organisation.person-already-member", "This person is already a member of the pharmacy.");
