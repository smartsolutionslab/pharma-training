using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListMyPharmacies;

public sealed class ListMyPharmaciesHandler(IPharmacyStore pharmacies)
    : IQueryHandler<ListMyPharmacies, Result<PagedResult<MyPharmacyDto>, ApiError>>
{
    public async Task<Result<PagedResult<MyPharmacyDto>, ApiError>> HandleAsync(ListMyPharmacies query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var page = await pharmacies.ListWithActiveMemberAsync(query.Person, query.Page, query.PageSize, cancellationToken);
        List<MyPharmacyDto> items = [.. page.Items.SelectMany(pharmacy => pharmacy.ActiveMembershipOf(query.Person)
            .Match<IEnumerable<MyPharmacyDto>>(membership => [pharmacy.ToMyPharmacyDto(membership)], () => []))];
        return Success(new PagedResult<MyPharmacyDto>(items, page.TotalCount, page.Page, page.PageSize));
    }
}
