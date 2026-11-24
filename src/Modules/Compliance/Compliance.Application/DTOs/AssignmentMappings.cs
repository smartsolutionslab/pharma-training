using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class AssignmentMappings
{
    public static ComplianceAssignmentDto ToDto(this Assignment assignment)
    {
        Ensure.That(assignment).IsNotNull();

        return new ComplianceAssignmentDto(
            assignment.Id.Value,
            assignment.Pharmacy.Value,
            assignment.Learner.Value,
            assignment.Course.Value,
            assignment.DueDate.Value,
            assignment.Recurrence?.Value,
            assignment.AssignedBy.Value,
            assignment.AssignedAt.Value,
            assignment.Status.ToDto(),
            assignment.StartedAt?.Value,
            assignment.CompletedAt?.Value,
            [.. assignment.History.Select(change => change.ToDto())]);
    }
}
