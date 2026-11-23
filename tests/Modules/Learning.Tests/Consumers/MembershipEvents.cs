using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>Organisation's membership events as they arrive: published by its unit of work, so each carries its metadata.</summary>
internal static class MembershipEvents
{
    private static readonly DateTimeOffset StartedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndedAt = new(2026, 11, 28, 17, 0, 0, TimeSpan.Zero);

    public static MembershipStartedV1 Started(Guid membershipId, Guid personId, Guid pharmacyId) =>
        new(membershipId, personId, pharmacyId, "Ada Lovelace", JobFunctionV1.Pta, MembershipRoleV1.Employee, StartedAt) { Metadata = NewMetadata() };

    public static MembershipChangedV1 Changed(Guid membershipId, Guid personId, Guid pharmacyId, int version) =>
        new(membershipId, personId, pharmacyId, "Ada Lovelace", JobFunctionV1.Pta, MembershipRoleV1.Manager, version) { Metadata = NewMetadata() };

    public static MembershipEndedV1 Ended(Guid membershipId, Guid personId, Guid pharmacyId) =>
        new(membershipId, personId, pharmacyId, EndedAt) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), StartedAt);
}
