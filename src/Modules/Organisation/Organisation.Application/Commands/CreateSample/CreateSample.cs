using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.CreateSample;

/// <summary>Creates a sample with this name and answers its id.</summary>
public sealed record CreateSample(SampleName Name) : ICommand<Result<Guid, ApiError>>;
