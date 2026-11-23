namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>What the dashboard shows on a given day: the stored status combined with the due date.</summary>
public enum AssignmentState
{
    Open,
    InProgress,
    Locked,
    Overdue,
    Completed,
    CompletedLate,
    Cancelled,
}
