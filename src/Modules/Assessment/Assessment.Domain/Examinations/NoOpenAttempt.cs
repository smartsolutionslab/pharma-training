namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

public sealed record NoOpenAttempt(ExaminationId Examination)
    : DomainError("assessment.no-open-attempt", "No test attempt is running at the moment.");
