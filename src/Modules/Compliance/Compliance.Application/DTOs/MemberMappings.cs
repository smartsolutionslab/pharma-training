using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class MemberMappings
{
    public static ComplianceMemberDto ToDto(this CurrentMembership membership)
    {
        Ensure.That(membership).IsNotNull();

        return new ComplianceMemberDto(membership.Person.Value, membership.Name?.Value ?? string.Empty, membership.JobFunction.ToDto());
    }
}
