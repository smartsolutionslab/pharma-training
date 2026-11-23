using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A command whose own members are harmless and whose result reveals the solution.</summary>
public sealed record ProbeSubmitRevealingSolution(Guid AttemptId) : ICommand<Result<ProbeSolutionResponse, ApiError>>;
