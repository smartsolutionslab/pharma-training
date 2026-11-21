using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

[DeliberatelyUnhandled("Shown in the editorial inbox through a query; no reaction needed.")]
public sealed record SubmittedForReview(CourseVersionId Version, PersonId SubmittedBy) : IDomainEvent;
