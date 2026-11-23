using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>One entry of the history a manager's changes leave behind.</summary>
public sealed record AssignmentChange(
    AssignmentChangeKind Kind,
    PersonId ChangedBy,
    Timestamp ChangedAt,
    DueDate? PreviousDueDate,
    DueDate? NewDueDate,
    ChangeReason? Reason);
