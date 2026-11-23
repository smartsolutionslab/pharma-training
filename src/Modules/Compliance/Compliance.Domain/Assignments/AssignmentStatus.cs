namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>The stored lifecycle. Overdue and late are never stored: they follow from the due date.</summary>
public enum AssignmentStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
}
