using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>An assignment's state under its own name in the endpoints; a cancelled one is never shown, and it or an undefined one is a bug.</summary>
public static class AssignmentStateMappings
{
    public static ComplianceAssignmentState ToDto(this AssignmentState state) => state switch
    {
        AssignmentState.Open => ComplianceAssignmentState.Open,
        AssignmentState.InProgress => ComplianceAssignmentState.InProgress,
        AssignmentState.Locked => ComplianceAssignmentState.Locked,
        AssignmentState.Overdue => ComplianceAssignmentState.Overdue,
        AssignmentState.Completed => ComplianceAssignmentState.Completed,
        AssignmentState.CompletedLate => ComplianceAssignmentState.CompletedLate,
        AssignmentState.Cancelled => throw new UnreachableException("A cancelled assignment is never shown."),
        _ => throw new UnreachableException($"Undefined assignment state {state}."),
    };
}
