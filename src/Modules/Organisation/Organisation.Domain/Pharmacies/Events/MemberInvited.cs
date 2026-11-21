using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("Until #9 adds the reaction that sends the invitation mail through the IdP.")]
public sealed record MemberInvited(PharmacyId Pharmacy, MembershipId Membership, EmailAddress Email, InvitationToken Token, Timestamp ExpiresAt) : IDomainEvent;
