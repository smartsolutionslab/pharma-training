using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// Viewing the team's compliance (#34) is a capability of its own, not assigning courses (docs/adr/0003); in MVP 1 the
/// manager holds it and an employee does not.
/// </summary>
public class CapabilityTests
{
    [Fact]
    public void A_manager_may_view_the_team_compliance()
    {
        MembershipRole.Manager.Grants(Capability.ViewTeamCompliance).ShouldBeTrue();
    }

    [Fact]
    public void An_employee_may_not()
    {
        MembershipRole.Employee.Grants(Capability.ViewTeamCompliance).ShouldBeFalse();
    }
}
