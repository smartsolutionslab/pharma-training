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
        Started(membershipId, personId, pharmacyId, displayName, MembershipRoleV1.Employee, startedAt);

    public static MembershipStartedV1 Started(Guid membershipId, Guid personId, Guid pharmacyId, string displayName, MembershipRoleV1 role, DateTimeOffset startedAt) =>
        new(membershipId, personId, pharmacyId, displayName, JobFunctionV1.Pta, role, startedAt) { Metadata = NewMetadata() };

    /// <summary>A change of the membership's role at the version, as Organisation counts them: the first change is 2.</summary>
    public static MembershipChangedV1 Changed(Guid membershipId, Guid personId, Guid pharmacyId, MembershipRoleV1 role, int version) =>
        new(membershipId, personId, pharmacyId, "Ada Lovelace", JobFunctionV1.Pta, role, version) { Metadata = NewMetadata() };

    public static MembershipEndedV1 Ended(Guid membershipId, Guid personId, Guid pharmacyId) =>
        new(membershipId, personId, pharmacyId, EndedAt) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), StartedAt);
}
