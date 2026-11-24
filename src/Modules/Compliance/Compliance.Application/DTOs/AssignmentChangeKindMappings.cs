using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A change's kind under its own name in the endpoints; an undefined one is a bug.</summary>
public static class AssignmentChangeKindMappings
{
    public static ComplianceAssignmentChangeKind ToDto(this AssignmentChangeKind kind) => kind switch
    {
        AssignmentChangeKind.DueDateChanged => ComplianceAssignmentChangeKind.DueDateChanged,
        AssignmentChangeKind.Cancelled => ComplianceAssignmentChangeKind.Cancelled,
        _ => throw new UnreachableException($"Undefined assignment change kind {kind}."),
    };
}
