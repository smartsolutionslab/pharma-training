namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationForAnotherAddress(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-for-another-address", "Die Einladung gilt für eine andere E-Mail-Adresse.");
