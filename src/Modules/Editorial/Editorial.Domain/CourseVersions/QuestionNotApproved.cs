namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record QuestionNotApproved(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-not-approved", "Every question must be approved before publishing.");
