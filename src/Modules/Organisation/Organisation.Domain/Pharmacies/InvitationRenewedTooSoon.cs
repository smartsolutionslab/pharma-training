namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Renewing an invitation within its cooldown; it may be renewed from <paramref name="RetryAt"/>; refused at <paramref name="RefusedAt"/>, on the clock <paramref name="RetryAt"/> was computed on.</summary>
public sealed record InvitationRenewedTooSoon(PharmacyId Pharmacy, MembershipId Membership, Timestamp RetryAt, Timestamp RefusedAt)
    : DomainError("organisation.invitation-renewed-too-soon", "The invitation was mailed a moment ago; renew it once the waiting time has passed.");
