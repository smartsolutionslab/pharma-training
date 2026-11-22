namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record TooFewOptions(QuestionId Question)
    : CourseVersionWorkflowError("editorial.too-few-options", "Eine Frage braucht mindestens zwei Antwortoptionen.");
