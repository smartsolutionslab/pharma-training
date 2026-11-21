using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

[DeliberatelyUnhandled("The publish/retire use case announces it to other modules (CourseVersionPublishedV1 / CourseVersionRetiredV1); nothing in Editorial reacts.")]
public sealed record CourseVersionPublished(CourseVersionId Version, CourseId Course, VersionNumber Number, Timestamp PublishedAt) : IDomainEvent;
