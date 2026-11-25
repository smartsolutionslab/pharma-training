using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// The messages Compliance stores that carry a person's name (#115, #439): purged for an erased person by the erasure,
/// by a consumer that drops a tombstoned person's message, and by the module's sweep of erased persons' messages.
/// </summary>
public static class PersonalMessages
{
    public static readonly IReadOnlyCollection<Type> Types = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];
}
