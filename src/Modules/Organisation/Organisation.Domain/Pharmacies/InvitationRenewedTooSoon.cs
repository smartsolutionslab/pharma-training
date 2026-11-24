namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Renewing an invitation within its cooldown; it may be renewed from <paramref name="RetryAt"/>.</summary>
public sealed record InvitationRenewedTooSoon(PharmacyId Pharmacy, MembershipId Membership, Timestamp RetryAt)
    : DomainError("organisation.invitation-renewed-too-soon", "The invitation was mailed a moment ago; renew it once the waiting time has passed.");
