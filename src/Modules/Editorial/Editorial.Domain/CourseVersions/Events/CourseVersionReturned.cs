using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

[DeliberatelyUnhandled("Audit only: the version records who returned it and when; nothing in Editorial or another module reacts.")]
public sealed record CourseVersionReturned(CourseVersionId Version, PersonId ReturnedBy) : IDomainEvent;
