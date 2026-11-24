namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

public sealed record AssessmentAttemptQuestionDto(
    Guid Id,
    AssessmentQuestionType Type,
    string Text,
    IReadOnlyList<AssessmentAnswerOptionDto> Options,
    IReadOnlyList<Guid> ChosenOptionIds);
