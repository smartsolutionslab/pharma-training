using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// Learning's copy of the memberships: their starts, changes and endings, each only ever inserted. Implemented in its
/// Infrastructure project; committing the module's unit of work stores what is added. Copied from Compliance's
/// <c>Compliance.Application/IMemberStore.cs</c>.
/// </summary>
public interface IMembershipCopies
{
    /// <summary>Whether the membership's start is stored.</summary>
    Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(MemberCopy member);

    /// <summary>Whether the membership's ending is stored.</summary>
    Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(MembershipEndingCopy ending);

    /// <summary>Whether the membership's change at that version is stored.</summary>
    Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken);

    void Add(MembershipChangeCopy change);

    /// <summary>Every start, change and ending of the person's memberships, in any pharmacy.</summary>
    Task<PersonMemberships> OfPersonAsync(PersonId person, CancellationToken cancellationToken);
}
