using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.CanManage;

/// <summary>Whether the caller may manage learning: the endpoint that sends it requires the policy, so it answers only a holder.</summary>
public sealed record CanManage : IQuery<Result<PermissionDto, ApiError>>;
