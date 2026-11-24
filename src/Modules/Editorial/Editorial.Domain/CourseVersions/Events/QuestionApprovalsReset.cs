using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;

/// <summary>
/// At least one approved question of the version went back to review, because of what <paramref name="ResetBy"/> did
/// (#246). Raised once per change, and only when an approval was actually reset.
/// </summary>
[DeliberatelyUnhandled("Audit marker, like AiTestDraftApplied. The four-eyes rule reads BlueprintChangedBy, saved in the version's own save; a reaction runs after the commit and is never retried, so none could keep a reliable trail. A stored reset history would be rows of its own, as returns are (ADR 0013).")]
public sealed record QuestionApprovalsReset(CourseVersionId Version, PersonId ResetBy, ApprovalResetReason Reason) : IDomainEvent;
