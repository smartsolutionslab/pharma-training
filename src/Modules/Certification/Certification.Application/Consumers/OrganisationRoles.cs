using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>Organisation's roles as Certification holds them; the consumers' guards refuse an undefined value first.</summary>
internal static class OrganisationRoles
{
    /// <summary>Refuses a role Organisation does not define, by the field's name, so a poison message says which field.</summary>
    public static void Guard(MembershipRoleV1 role, string name) =>
        Ensure.That((int)role, name).Satisfies(value => Enum.IsDefined((MembershipRoleV1)value), "is no role Certification knows.");

    public static MembershipRole ToRole(MembershipRoleV1 role) => role switch
    {
        MembershipRoleV1.Manager => MembershipRole.Manager,
        MembershipRoleV1.Employee => MembershipRole.Employee,
        _ => throw new InvalidOperationException($"Role {role} has no counterpart in Certification."),
    };
}
