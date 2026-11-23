using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// The matrix's cells counted, how many of them are compliant today and how many overdue. An open, in-progress or
/// locked cell is compliant, an overdue one is not, and a completed one (late or not) is compliant while today is on or
/// before its valid-until day. The compliance rate is the share of compliant cells; without a cell there is no rate.
/// </summary>
public sealed record AssignmentTally(int Assignments, int Compliant, int Overdue)
{
    public double? ComplianceRate => Assignments == 0 ? null : (double)Compliant / Assignments;

    public static AssignmentTally Of(IEnumerable<ComplianceMatrixCell> cells, CalendarDate today)
    {
        Ensure.That(cells).IsNotNull();
        Ensure.That(today).IsNotNull();

        var assignments = 0;
        var compliant = 0;
        var overdue = 0;
        foreach (var cell in cells)
        {
            assignments++;
            if (IsCompliant(cell, today))
            {
                compliant++;
            }

            if (cell.State == AssignmentState.Overdue)
            {
                overdue++;
            }
        }

        return new AssignmentTally(assignments, compliant, overdue);
    }

    private static bool IsCompliant(ComplianceMatrixCell cell, CalendarDate today) => cell.State switch
    {
        AssignmentState.Open or AssignmentState.InProgress or AssignmentState.Locked => true,
        AssignmentState.Completed or AssignmentState.CompletedLate => cell.ValidUntil is { } validUntil && today <= validUntil,
        _ => false,
    };
}
