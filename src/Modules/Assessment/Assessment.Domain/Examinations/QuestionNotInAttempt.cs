namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record QuestionNotInAttempt(ExaminationId Examination, QuestionVersionId Question)
    : DomainError("assessment.question-not-in-attempt", "This question does not belong to this attempt.");
