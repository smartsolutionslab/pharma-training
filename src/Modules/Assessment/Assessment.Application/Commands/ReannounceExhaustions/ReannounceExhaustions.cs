using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceExhaustions;

/// <summary>One pass of Assessment's sweep of locks (#307): announce every stored exhaustion whose announcement never left.</summary>
public sealed record ReannounceExhaustions : ICommand<Result<Unit, ApiError>>;
