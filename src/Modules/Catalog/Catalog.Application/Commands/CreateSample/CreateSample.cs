using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Commands.CreateSample;

/// <summary>Creates a sample with this name, which the use case checks, and answers its id.</summary>
public sealed record CreateSample(string Name) : ICommand<Result<Guid, ApiError>>;
