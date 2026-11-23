using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>Organisation's job functions and roles as Compliance holds them; the consumers' guards refuse an undefined value first.</summary>
internal static class OrganisationValues
{
    public static JobFunction ToJobFunction(JobFunctionV1 jobFunction) => jobFunction switch
    {
        JobFunctionV1.Pharmacist => JobFunction.Pharmacist,
        JobFunctionV1.Pta => JobFunction.Pta,
        JobFunctionV1.Pka => JobFunction.Pka,
        JobFunctionV1.Trainee => JobFunction.Trainee,
        JobFunctionV1.Other => JobFunction.Other,
        _ => throw new InvalidOperationException($"Job function {jobFunction} has no counterpart in Compliance."),
    };

    public static MembershipRole ToRole(MembershipRoleV1 role) => role switch
    {
        MembershipRoleV1.Manager => MembershipRole.Manager,
        MembershipRoleV1.Employee => MembershipRole.Employee,
        _ => throw new InvalidOperationException($"Role {role} has no counterpart in Compliance."),
    };
}
