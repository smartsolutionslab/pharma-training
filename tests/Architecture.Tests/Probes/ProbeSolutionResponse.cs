namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A response that hands the learner the solution under a name without "Correct".</summary>
public sealed record ProbeSolutionResponse(Guid QuestionId, string Solution);
