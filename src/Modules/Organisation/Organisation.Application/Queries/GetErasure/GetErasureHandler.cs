using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetErasure;

public sealed class GetErasureHandler(IErasureStore erasures) : IQueryHandler<GetErasure, Result<OrganisationErasureDto, ApiError>>
{
    private readonly IErasureStore erasures = erasures;

    public Task<Result<OrganisationErasureDto, ApiError>> HandleAsync(GetErasure query, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
