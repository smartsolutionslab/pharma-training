using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("The open invitation simply disappears from the list; nobody outside Organisation cares.")]
public sealed record InvitationRevoked(PharmacyId Pharmacy, MembershipId Membership) : IDomainEvent;
