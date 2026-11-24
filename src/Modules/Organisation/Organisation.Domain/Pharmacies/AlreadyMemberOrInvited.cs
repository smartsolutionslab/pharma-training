namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record AlreadyMemberOrInvited(PharmacyId Pharmacy, EmailAddress Email)
    : DomainError("organisation.already-member-or-invited", "This address is already a member or invited.");
