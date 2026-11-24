using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetAssignment;

/// <summary>
/// Reads an assignment of the pharmacy with its history (who changed what when), whatever its status, once the caller
/// may assign courses there (docs/adr/0003). One of another pharmacy is not found through this one.
/// </summary>
public sealed class GetAssignmentHandler(IMemberStore members, IAssignmentStore assignments)
    : IQueryHandler<GetAssignment, Result<ComplianceAssignmentDto, ApiError>>
{
    public async Task<Result<ComplianceAssignmentDto, ApiError>> HandleAsync(GetAssignment query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var access = await ComplianceAccess.RequireAsync(members, query.By, query.Pharmacy, Capability.AssignCourses, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var found = await assignments.ReadInPharmacyAsync(query.Pharmacy, query.Assignment, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(AssignmentFailures.AssignmentNotFound(query.Assignment.Value));
        }

        return Success(found.Value.ToDto());
    }
}
