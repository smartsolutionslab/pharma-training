using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// Builds the team matrix after checking the caller's capability (docs/adr/0003). A pharmacy has tens of members and
/// courses, so its memberships and assignments are resolved in memory: each cell's state is the assignment's own
/// <see cref="Assignment.StateOn"/> on today's German day, a domain rule no SQL projection repeats.
/// </summary>
public sealed class GetComplianceMatrixHandler(IMemberStore members, IAssignmentStore assignments, ICourseVersionStore courses, TimeProvider time)
    : IQueryHandler<GetComplianceMatrix, Result<ComplianceMatrixDto, ApiError>>
{
    public async Task<Result<ComplianceMatrixDto, ApiError>> HandleAsync(GetComplianceMatrix query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var access = await ComplianceAccess.RequireAsync(members, query.By, query.Pharmacy, Capability.ViewTeamCompliance, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var today = Timestamp.From(time.GetUtcNow()).DayInGermany();
        var memberships = await members.ActiveMembershipsInPharmacyAsync(query.Pharmacy, cancellationToken);

        // A person with two active memberships (an ending still on its way) is one row, from the newest: membership
        // ids are Guid v7, so the greatest is the latest.
        List<CurrentMembership> rows =
        [
            .. memberships
                .GroupBy(membership => membership.Person)
                .Select(person => person.OrderByDescending(membership => membership.Membership.Value).First())
                .Where(membership => query.JobFunction is not { } jobFunction || membership.JobFunction == jobFunction),
        ];

        var inPharmacy = await assignments.InPharmacyAsync(query.Pharmacy, cancellationToken);
        var published = await courses.FindCoursesAsync([.. inPharmacy.Select(assignment => assignment.Course).Distinct()], cancellationToken);

        var matrix = ComplianceMatrix.Of(rows, inPharmacy, published, today);
        return Success(matrix.ToDto(query.Page, query.PageSize));
    }
}
