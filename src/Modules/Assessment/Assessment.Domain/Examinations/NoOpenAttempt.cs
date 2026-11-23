namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record NoOpenAttempt(ExaminationId Examination)
    : DomainError("assessment.no-open-attempt", "Es läuft gerade kein Testversuch.");
