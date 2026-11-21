namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationExpired(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-expired", "Die Einladung ist abgelaufen. Bitte eine neue anfordern.");
