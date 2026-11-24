using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// Exporting a pharmacy's certificates is a capability (docs/adr/0003); in MVP 1 the manager holds every capability and
/// an employee none, as in Organisation and Compliance.
/// </summary>
public class RoleCapabilitiesTests
{
    [Fact]
    public void A_manager_may_export_the_pharmacys_certificates()
    {
        MembershipRole.Manager.Grants(Capability.ExportCertificates).ShouldBeTrue();
    }

    [Fact]
    public void An_employee_may_not()
    {
        MembershipRole.Employee.Grants(Capability.ExportCertificates).ShouldBeFalse();
    }

    [Fact]
    public void No_role_grants_a_capability_that_is_not_defined()
    {
        MembershipRole.Manager.Grants((Capability)99).ShouldBeFalse();
        MembershipRole.Employee.Grants((Capability)99).ShouldBeFalse();
    }
}
