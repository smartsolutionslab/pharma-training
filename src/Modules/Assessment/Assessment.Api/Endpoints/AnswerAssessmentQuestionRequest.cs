namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api.Endpoints;

/// <summary>The options the learner chose for a question of the open attempt: at least one, each once.</summary>
public sealed record AnswerAssessmentQuestionRequest(IReadOnlyList<Guid> OptionIds);
