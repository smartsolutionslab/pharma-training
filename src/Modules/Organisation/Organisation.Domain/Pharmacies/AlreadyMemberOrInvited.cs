namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record AlreadyMemberOrInvited(PharmacyId Pharmacy, EmailAddress Email)
    : DomainError("organisation.already-member-or-invited", "Diese Adresse ist schon Mitglied oder eingeladen.");
