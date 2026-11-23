using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;

/// <summary>Rows of Learning's copy of the memberships, as its consumers store them: a start, a change and an ending.</summary>
internal static class MembershipCopyRows
{
    /// <summary>The start of a new membership of the person in a pharmacy of its own.</summary>
    public static MemberCopy Started(PersonId person) =>
        new() { Id = Guid.CreateVersion7(), PersonId = person.Value, PharmacyId = Guid.CreateVersion7() };

    public static MembershipChangeCopy ChangeOf(MemberCopy member, int version) =>
        new() { MembershipId = member.Id, Version = version, PersonId = member.PersonId, PharmacyId = member.PharmacyId };

    public static MembershipEndingCopy EndingOf(MemberCopy member) =>
        new() { Id = member.Id, PersonId = member.PersonId, PharmacyId = member.PharmacyId };
}
