using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReannounceContentCompletions;

/// <summary>One pass of Learning's announcement sweep (#277): announce every stored completion whose announcement never left.</summary>
public sealed record ReannounceContentCompletions : ICommand<Result<Unit, ApiError>>;
