using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("No other module needs a change of job function or role in MVP 1 (docs/domain README, Integration Events); rule-based assignments will announce it as MembershipChangedV1 when they come.")]
public sealed record MembershipChanged(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, JobFunction JobFunction, MembershipRole Role) : IDomainEvent;
