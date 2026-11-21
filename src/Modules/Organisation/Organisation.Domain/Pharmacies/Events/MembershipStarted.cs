using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("The command that starts the membership announces it to other modules as MembershipStartedV1; nothing in Organisation reacts.")]
public sealed record MembershipStarted(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, DisplayName Name, JobFunction JobFunction, MembershipRole Role, Timestamp StartedAt) : IDomainEvent;
