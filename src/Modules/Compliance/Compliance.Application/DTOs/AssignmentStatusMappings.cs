using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A stored status under its own name in the endpoints; an undefined one is a bug.</summary>
public static class AssignmentStatusMappings
{
    public static ComplianceAssignmentStatus ToDto(this AssignmentStatus status) => status switch
    {
        AssignmentStatus.Open => ComplianceAssignmentStatus.Open,
        AssignmentStatus.InProgress => ComplianceAssignmentStatus.InProgress,
        AssignmentStatus.Completed => ComplianceAssignmentStatus.Completed,
        AssignmentStatus.Cancelled => ComplianceAssignmentStatus.Cancelled,
        _ => throw new UnreachableException($"Undefined assignment status {status}."),
    };
}
