using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("The command that changes the membership announces it to other modules as MembershipChangedV1; nothing in Organisation reacts.")]
public sealed record MembershipChanged(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, DisplayName Name, JobFunction JobFunction, MembershipRole Role, MembershipVersion Version) : IDomainEvent;
