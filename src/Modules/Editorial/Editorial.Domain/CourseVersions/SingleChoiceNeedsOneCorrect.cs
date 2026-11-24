namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record SingleChoiceNeedsOneCorrect(QuestionId Question)
    : DomainError("editorial.single-choice-one-correct", "A single-choice question has exactly one correct answer.");
