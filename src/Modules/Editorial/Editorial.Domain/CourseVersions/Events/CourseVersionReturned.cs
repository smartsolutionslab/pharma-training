using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

[DeliberatelyUnhandled("The return use case stores the CourseVersionReturn in the version's own save (ADR 0013); a reaction runs after the commit and is never retried, so none keeps the audit trail.")]
public sealed record CourseVersionReturned(CourseVersionId Version, PersonId ReturnedBy, ReturnReason Reason) : IDomainEvent;
