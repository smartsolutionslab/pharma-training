using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class AssignmentChangeMappings
{
    /// <summary>A history entry of the learner's assignment; one the learner made themselves is a self-change (#284).</summary>
    public static ComplianceAssignmentChangeDto ToDto(this AssignmentChange change, PersonId learner)
    {
        Ensure.That(change).IsNotNull();
        Ensure.That(learner).IsNotNull();

        return new ComplianceAssignmentChangeDto(
            change.Kind.ToDto(),
            change.ChangedBy.Value,
            change.ChangedAt.Value,
            change.PreviousDueDate?.Value,
            change.NewDueDate?.Value,
            change.Reason?.Value,
            change.ChangedBy == learner);
    }
}
