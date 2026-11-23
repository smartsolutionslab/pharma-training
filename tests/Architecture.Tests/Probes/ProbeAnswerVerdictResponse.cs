namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A response that tells the learner whether an answer was right: the solution leaves the service.</summary>
public sealed record ProbeAnswerVerdictResponse(Guid QuestionId, bool IsCorrect);
