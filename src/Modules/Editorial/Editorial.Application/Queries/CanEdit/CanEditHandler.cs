using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.CanEdit;

/// <summary>Names the permission the caller holds; the policy on the endpoint has already refused anyone else.</summary>
public sealed class CanEditHandler : IQueryHandler<CanEdit, Result<PermissionDto, ApiError>>
{
    public Task<Result<PermissionDto, ApiError>> HandleAsync(CanEdit query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        return Task.FromResult<Result<PermissionDto, ApiError>>(Success(EditorialPolicyNames.Edit.ToDto()));
    }
}
