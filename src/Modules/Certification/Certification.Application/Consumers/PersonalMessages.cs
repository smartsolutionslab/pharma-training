using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// The messages Certification stores that carry a person's name (#115, #439); <c>TestPassedV1</c> carries the id only.
/// They are purged for an erased person by the erasure and by the module's sweep of erased persons' messages, which also
/// removes the handled envelope of one a consumer dropped.
/// </summary>
public static class PersonalMessages
{
    public static readonly IReadOnlyCollection<Type> Types = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];
}
