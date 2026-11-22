using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

/// <summary>An address was invited, or its invitation renewed, valid until <see cref="ExpiresAt"/>. Carries no token, address or name.</summary>
[DeliberatelyUnhandled("The command that invites or renews publishes InvitationMailRequestedV1; its consumer issues the link and mails it.")]
public sealed record MemberInvited(PharmacyId Pharmacy, MembershipId Membership, Timestamp ExpiresAt) : IDomainEvent;
