namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

/// <summary>An attempt's questions in its order, each with its options in their derived order and the learner's last answer.</summary>
public sealed record AssessmentAttemptDto(Guid Id, int Number, IReadOnlyList<AssessmentAttemptQuestionDto> Questions);
