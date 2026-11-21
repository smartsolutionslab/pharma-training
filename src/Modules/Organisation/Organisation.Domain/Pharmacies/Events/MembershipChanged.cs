using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("No module consumes a change of job function or role yet. #29 announces it as MembershipChangedV1 so Compliance's copy of the role stays current (decided on #29); until then nothing outside Organisation learns of it.")]
public sealed record MembershipChanged(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, JobFunction JobFunction, MembershipRole Role) : IDomainEvent;
