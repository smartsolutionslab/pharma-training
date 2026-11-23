using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyMembers;

/// <summary>Lists them only for an active member whose role may manage members (ADR 0003): anyone else is told the pharmacy does not exist, or may not.</summary>
public sealed class ListPharmacyMembersHandler(IPharmacyStore pharmacies)
    : IQueryHandler<ListPharmacyMembers, Result<PagedResult<PharmacyMemberDto>, ApiError>>
{
    public async Task<Result<PagedResult<PharmacyMemberDto>, ApiError>> HandleAsync(ListPharmacyMembers query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var found = await pharmacies.FindAsync(query.Pharmacy, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(PharmacyFailures.NotFound(query.Pharmacy.Value));
        }

        var access = found.Value.Require(query.By, Capability.ManageMembers);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var page = await pharmacies.ListActiveMembershipsAsync(query.Pharmacy, query.Page, query.PageSize, cancellationToken);
        List<PharmacyMemberDto> items = [.. page.Items.Select(membership => membership.ToPharmacyMemberDto())];
        return Success(new PagedResult<PharmacyMemberDto>(items, page.TotalCount, page.Page, page.PageSize));
    }
}
