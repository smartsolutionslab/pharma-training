using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.CanEdit;

/// <summary>Whether the caller may edit editorial content: the endpoint that sends it requires the policy, so it answers only a holder.</summary>
public sealed record CanEdit : IQuery<Result<PermissionDto, ApiError>>;
