namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record SingleChoiceNeedsOneCorrect(QuestionId Question)
    : CourseVersionWorkflowError("editorial.single-choice-one-correct", "Eine Single-Choice-Frage hat genau eine richtige Antwort.");
