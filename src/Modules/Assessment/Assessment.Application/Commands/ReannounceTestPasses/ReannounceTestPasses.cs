using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestPasses;

/// <summary>One pass of Assessment's announcement sweep (#277): announce every stored test pass whose announcement never left.</summary>
public sealed record ReannounceTestPasses : ICommand<Result<Unit, ApiError>>;
