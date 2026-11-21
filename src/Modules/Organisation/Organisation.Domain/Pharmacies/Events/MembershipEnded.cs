using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("The command that ends the membership announces it to other modules as MembershipEndedV1; nothing in Organisation reacts.")]
public sealed record MembershipEnded(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, Timestamp EndedAt) : IDomainEvent;
