namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

public sealed record AssessmentAttemptResultDto(
    Guid AttemptId,
    int Number,
    int ScorePercent,
    int PassingScorePercent,
    AssessmentAttemptOutcome Outcome,
    IReadOnlyList<Guid> ChaptersToRepeat,
    int AttemptsLeft);
