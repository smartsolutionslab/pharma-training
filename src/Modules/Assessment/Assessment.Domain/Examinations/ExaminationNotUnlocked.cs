namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record ExaminationNotUnlocked(ExaminationId Examination, ExaminationStatus Status)
    : DomainError("assessment.examination-not-unlocked", "Der Test ist gerade nicht freigeschaltet.");
