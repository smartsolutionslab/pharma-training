using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>A membership role under its own name in the endpoints and the integration events; an undefined one is a bug.</summary>
public static class MembershipRoleMappings
{
    public static OrganisationMembershipRole ToDto(this MembershipRole role) => role switch
    {
        MembershipRole.Manager => OrganisationMembershipRole.Manager,
        MembershipRole.Employee => OrganisationMembershipRole.Employee,
        _ => throw new UnreachableException($"Undefined membership role {role}."),
    };

    public static MembershipRoleV1 ToV1(this MembershipRole role) => role switch
    {
        MembershipRole.Manager => MembershipRoleV1.Manager,
        MembershipRole.Employee => MembershipRoleV1.Employee,
        _ => throw new UnreachableException($"Undefined membership role {role}."),
    };
}
