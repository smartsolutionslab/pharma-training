namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record QuestionNotApproved(CourseVersionId Version, QuestionId Question)
    : CourseVersionWorkflowError("editorial.question-not-approved", "Jede Frage muss vor der Veröffentlichung freigegeben sein.");
