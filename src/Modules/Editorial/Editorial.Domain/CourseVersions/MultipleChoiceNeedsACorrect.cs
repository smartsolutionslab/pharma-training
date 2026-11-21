namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record MultipleChoiceNeedsACorrect(QuestionId Question)
    : DomainError("editorial.multiple-choice-a-correct", "Eine Multiple-Choice-Frage hat mindestens eine richtige Antwort.");
