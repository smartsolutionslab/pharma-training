using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetMyPharmacy;

public sealed class GetMyPharmacyHandler(IPharmacyStore pharmacies)
    : IQueryHandler<GetMyPharmacy, Result<MyPharmacyDto, ApiError>>
{
    public async Task<Result<MyPharmacyDto, ApiError>> HandleAsync(GetMyPharmacy query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(PharmacyFailures.NotFound(query.Id));
        }

        var found = await pharmacies.FindAsync(PharmacyId.From(query.Id), cancellationToken);
        if (!found.HasValue)
        {
            return Failure(PharmacyFailures.NotFound(query.Id));
        }

        var pharmacy = found.Value;
        var membership = pharmacy.ActiveMembershipOf(query.Person);
        if (!membership.HasValue)
        {
            return Failure(PharmacyFailures.NotFound(query.Id));
        }

        return Success(pharmacy.ToMyPharmacyDto(membership.Value));
    }
}
