using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// The active assignments (open, in progress, locked or overdue) and how many of them are overdue. The compliance rate
/// is the share of active ones that are not overdue; without an active one there is nothing to be compliant with, so
/// there is no rate.
/// </summary>
public sealed record AssignmentTally(int Active, int Overdue)
{
    public double? ComplianceRate => Active == 0 ? null : (double)(Active - Overdue) / Active;

    public static AssignmentTally Of(IEnumerable<AssignmentState> states)
    {
        Ensure.That(states).IsNotNull();

        var active = 0;
        var overdue = 0;
        foreach (var state in states)
        {
            if (state is AssignmentState.Open or AssignmentState.InProgress or AssignmentState.Locked or AssignmentState.Overdue)
            {
                active++;
            }

            if (state == AssignmentState.Overdue)
            {
                overdue++;
            }
        }

        return new AssignmentTally(active, overdue);
    }
}
