namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>An approved question is not approved again: the first approver and the time stay recorded until an edit takes the approval away.</summary>
public sealed record QuestionAlreadyApproved(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-already-approved", "This question is already approved; edit it to have it approved again.");
