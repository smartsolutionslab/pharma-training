using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

/// <summary>
/// The compliance rate (#34): the share of active assignments, those that are open, in progress, locked or overdue,
/// that are not overdue; without an active assignment there is nothing to be compliant with, so no rate.
/// </summary>
public class AssignmentTallyTests
{
    [Fact]
    public void Open_in_progress_locked_and_overdue_are_active()
    {
        var tally = AssignmentTally.Of([AssignmentState.Open, AssignmentState.InProgress, AssignmentState.Locked, AssignmentState.Overdue]);

        tally.Active.ShouldBe(4);
        tally.Overdue.ShouldBe(1);
        tally.ComplianceRate.ShouldBe(0.75);
    }

    [Fact]
    public void Completed_completed_late_and_cancelled_are_not_active_and_leave_no_rate()
    {
        var tally = AssignmentTally.Of([AssignmentState.Completed, AssignmentState.CompletedLate, AssignmentState.Cancelled]);

        tally.Active.ShouldBe(0);
        tally.Overdue.ShouldBe(0);
        tally.ComplianceRate.ShouldBeNull();
    }

    [Fact]
    public void No_states_leave_no_rate()
    {
        var tally = AssignmentTally.Of([]);

        tally.Active.ShouldBe(0);
        tally.Overdue.ShouldBe(0);
        tally.ComplianceRate.ShouldBeNull();
    }
}
