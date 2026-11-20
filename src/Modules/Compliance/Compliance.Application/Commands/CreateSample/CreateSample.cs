using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.CreateSample;

/// <summary>Creates a sample with this name and answers its id.</summary>
public sealed record CreateSample(SampleName Name) : ICommand<Result<Guid, ApiError>>;
