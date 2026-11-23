using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A query whose own members are harmless and whose result reaches an answer key.</summary>
public sealed record ProbeGetLeakyPaper(Guid AttemptId) : IQuery<Result<ProbeLeakyPaper, ApiError>>;
