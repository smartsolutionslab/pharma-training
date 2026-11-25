using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetErasure;

public sealed class GetErasureHandler(IErasureStore erasures) : IQueryHandler<GetErasure, Result<OrganisationErasureDto, ApiError>>
{
    private readonly IErasureStore erasures = erasures;

    public async Task<Result<OrganisationErasureDto, ApiError>> HandleAsync(GetErasure query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(ErasureFailures.NotFound(query.Id));
        }

        var found = await erasures.FindAsync(ErasureId.From(query.Id), cancellationToken);
        return found switch
        {
            Some<Erasure> some => Success(some.Value.ToDto()),
            None => Failure(ErasureFailures.NotFound(query.Id)),
        };
    }
}
