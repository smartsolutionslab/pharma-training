namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record ExaminationNotUnlocked(ExaminationId Examination, ExaminationStatus Status)
    : DomainError("assessment.examination-not-unlocked", "The test is not unlocked at the moment.");
