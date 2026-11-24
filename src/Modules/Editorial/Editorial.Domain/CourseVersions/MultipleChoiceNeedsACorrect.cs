namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record MultipleChoiceNeedsACorrect(QuestionId Question)
    : DomainError("editorial.multiple-choice-a-correct", "A multiple-choice question has at least one correct answer.");
