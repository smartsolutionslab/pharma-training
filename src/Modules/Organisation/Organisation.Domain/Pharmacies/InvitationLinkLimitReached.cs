namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

public sealed record InvitationLinkLimitReached(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-link-limit-reached", "The invitation already holds as many links as one invitation version may; renew it to issue a new one.");
