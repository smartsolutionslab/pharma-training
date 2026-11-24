namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

/// <summary>A learner's examination as its client reads it by session; never a solution.</summary>
public sealed record AssessmentExaminationDto(
    Guid Id,
    Guid SessionId,
    Guid CourseId,
    Guid CourseVersionId,
    AssessmentExaminationStatus Status,
    int AttemptsUsed,
    int? MaxAttempts,
    AssessmentAttemptDto? CurrentAttempt,
    AssessmentAttemptResultDto? LastResult);
