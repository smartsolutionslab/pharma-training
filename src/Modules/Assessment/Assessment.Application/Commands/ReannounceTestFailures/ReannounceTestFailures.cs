using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestFailures;

/// <summary>One pass of Assessment's sweep of failures (#307): announce every stored failed attempt whose announcement never left.</summary>
public sealed record ReannounceTestFailures : ICommand<Result<Unit, ApiError>>;
