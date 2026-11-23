using System.Diagnostics;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

/// <summary>
/// The six states an assignment shows on the wire (#34), each under its own name; a cancelled assignment is never shown,
/// so it has no state there.
/// </summary>
public class AssignmentStateMappingsTests
{
    [Theory]
    [InlineData(AssignmentState.Open, ComplianceAssignmentState.Open)]
    [InlineData(AssignmentState.InProgress, ComplianceAssignmentState.InProgress)]
    [InlineData(AssignmentState.Locked, ComplianceAssignmentState.Locked)]
    [InlineData(AssignmentState.Overdue, ComplianceAssignmentState.Overdue)]
    [InlineData(AssignmentState.Completed, ComplianceAssignmentState.Completed)]
    [InlineData(AssignmentState.CompletedLate, ComplianceAssignmentState.CompletedLate)]
    public void Each_shown_state_maps_to_the_state_of_the_same_name(AssignmentState state, ComplianceAssignmentState expected)
    {
        state.ToDto().ShouldBe(expected);
        expected.ToString().ShouldBe(state.ToString());
    }

    [Fact]
    public void A_cancelled_assignment_is_never_mapped()
    {
        Should.Throw<UnreachableException>(() => AssignmentState.Cancelled.ToDto());
    }

    [Fact]
    public void The_state_travels_by_name()
    {
        JsonSerializer.Serialize(ComplianceAssignmentState.CompletedLate).ShouldBe("\"CompletedLate\"");
    }
}
