using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>Organisation's membership events as they arrive: published by its unit of work, so each carries its metadata.</summary>
internal static class MembershipEvents
{
    public static readonly DateTimeOffset StartedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset EndedAt = new(2026, 11, 28, 17, 0, 0, TimeSpan.Zero);

    public static MembershipStartedV1 Started(Guid membershipId, Guid personId, Guid pharmacyId, string displayName) =>
        Started(membershipId, personId, pharmacyId, displayName, StartedAt);

    public static MembershipStartedV1 Started(Guid membershipId, Guid personId, Guid pharmacyId, string displayName, DateTimeOffset startedAt) =>
        new(membershipId, personId, pharmacyId, displayName, JobFunctionV1.Pta, MembershipRoleV1.Employee, startedAt) { Metadata = NewMetadata() };

    public static MembershipEndedV1 Ended(Guid membershipId, Guid personId, Guid pharmacyId) =>
        new(membershipId, personId, pharmacyId, EndedAt) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), StartedAt);
}
