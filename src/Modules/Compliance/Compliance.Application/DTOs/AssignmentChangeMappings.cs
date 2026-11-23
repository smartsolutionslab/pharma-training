using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class AssignmentChangeMappings
{
    public static ComplianceAssignmentChangeDto ToDto(this AssignmentChange change)
    {
        Ensure.That(change).IsNotNull();

        return new ComplianceAssignmentChangeDto(
            change.Kind.ToDto(),
            change.ChangedBy.Value,
            change.ChangedAt.Value,
            change.PreviousDueDate?.Value,
            change.NewDueDate?.Value,
            change.Reason?.Value);
    }
}
