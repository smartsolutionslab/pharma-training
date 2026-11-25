using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListPharmacyMembers;

/// <summary>
/// Lists the pharmacy's active members as they stand now (docs/adr/0015), after checking the caller's capability like
/// the assign handler does (docs/adr/0003). A pharmacy has tens of staff, so its memberships are resolved, deduplicated,
/// filtered, sorted and paged in memory, through the same <see cref="CurrentMembership.Of"/> the assign check uses.
/// </summary>
public sealed class ListPharmacyMembersHandler(IMemberStore members)
    : IQueryHandler<ListPharmacyMembers, Result<PagedResult<ComplianceMemberDto>, ApiError>>
{
    public async Task<Result<PagedResult<ComplianceMemberDto>, ApiError>> HandleAsync(ListPharmacyMembers query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var access = await ComplianceAccess.RequireAsync(members, query.By, query.Pharmacy, Capability.AssignCourses, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var memberships = await members.ActiveMembershipsInPharmacyAsync(query.Pharmacy, cancellationToken);

        // A person with two active memberships (an ending still on its way) is one row, from the newest: membership
        // ids are Guid v7, so the greatest is the latest.
        var rows = memberships
            .GroupBy(membership => membership.Person)
            .Select(person => person.OrderByDescending(membership => membership.Membership.Value).First())
            .Where(membership => query.JobFunction is not { } jobFunction || membership.JobFunction == jobFunction)
            .OrderBy(membership => membership.Name?.Value ?? string.Empty, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(membership => membership.Person.Value)
            .ToList();

        var items = rows
            .Skip(query.Page.Skip(query.PageSize))
            .Take(query.PageSize.Value)
            .Select(membership => membership.ToDto());
        return Success(new PagedResult<ComplianceMemberDto>([.. items], rows.Count, query.Page.Value, query.PageSize.Value));
    }
}
