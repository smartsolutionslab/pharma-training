using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

/// <summary>
/// An address was invited, or its invitation renewed with a new link. Carries the raw token for the invitation mail;
/// never store, publish or log it. That is safe only because a domain event of this module lives in memory: it is
/// neither stored nor published, and <see cref="InvitationToken"/> does not print its value.
/// </summary>
public sealed record MemberInvited(PharmacyId Pharmacy, PharmacyName Name, MembershipId Membership, EmailAddress Email, InvitationToken Token, Timestamp ExpiresAt) : IDomainEvent;
