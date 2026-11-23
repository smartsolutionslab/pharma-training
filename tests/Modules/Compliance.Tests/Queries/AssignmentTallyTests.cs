using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

/// <summary>
/// The compliance rate (#34, as the owner decided on PR #254): the share of the matrix's cells that are compliant today.
/// An open, in-progress or locked cell is compliant, an overdue one is not, and a completed one (late or not) is compliant
/// while today is on or before its valid-until day; without a cell there is no rate.
/// </summary>
public class AssignmentTallyTests
{
    private static readonly CalendarDate Today = CalendarDate.From(new DateOnly(2026, 11, 23));

    [Fact]
    public void Open_in_progress_and_locked_cells_are_compliant_and_an_overdue_one_is_not()
    {
        var tally = AssignmentTally.Of([Cell(AssignmentState.Open), Cell(AssignmentState.InProgress), Cell(AssignmentState.Locked), Cell(AssignmentState.Overdue)], Today);

        tally.Assignments.ShouldBe(4);
        tally.Compliant.ShouldBe(3);
        tally.Overdue.ShouldBe(1);
        tally.ComplianceRate.ShouldBe(0.75);
    }

    [Fact]
    public void A_completed_cell_is_compliant_on_its_last_valid_day_and_not_the_day_after()
    {
        var tally = AssignmentTally.Of(
            [
                Cell(AssignmentState.Completed, validUntil: new DateOnly(2026, 11, 23)),
                Cell(AssignmentState.CompletedLate, validUntil: new DateOnly(2026, 11, 22)),
            ],
            Today);

        tally.Assignments.ShouldBe(2);
        tally.Compliant.ShouldBe(1);
        tally.Overdue.ShouldBe(0);
        tally.ComplianceRate.ShouldBe(0.5);
    }

    [Fact]
    public void A_fully_trained_team_is_entirely_compliant()
    {
        var tally = AssignmentTally.Of(
            [
                Cell(AssignmentState.Completed, validUntil: new DateOnly(2027, 11, 10)),
                Cell(AssignmentState.CompletedLate, validUntil: new DateOnly(2027, 1, 5)),
                Cell(AssignmentState.Completed, validUntil: new DateOnly(2026, 11, 23)),
            ],
            Today);

        tally.Assignments.ShouldBe(3);
        tally.Compliant.ShouldBe(3);
        tally.Overdue.ShouldBe(0);
        tally.ComplianceRate.ShouldBe(1.0);
    }

    [Fact]
    public void No_cells_leave_no_rate()
    {
        var tally = AssignmentTally.Of([], Today);

        tally.Assignments.ShouldBe(0);
        tally.Compliant.ShouldBe(0);
        tally.Overdue.ShouldBe(0);
        tally.ComplianceRate.ShouldBeNull();
    }

    private static ComplianceMatrixCell Cell(AssignmentState state, DateOnly? validUntil = null) =>
        new(
            CourseId.From(Guid.CreateVersion7()),
            AssignmentId.New(),
            state,
            DueDate.From(new DateOnly(2026, 12, 15)),
            null,
            validUntil is { } day ? CalendarDate.From(day) : null);
}
