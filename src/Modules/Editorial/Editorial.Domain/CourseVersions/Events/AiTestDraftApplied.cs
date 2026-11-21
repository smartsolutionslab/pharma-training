using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

[DeliberatelyUnhandled("Audit only.")]
public sealed record AiTestDraftApplied(CourseVersionId Version, QuestionCount Drafted) : IDomainEvent;
