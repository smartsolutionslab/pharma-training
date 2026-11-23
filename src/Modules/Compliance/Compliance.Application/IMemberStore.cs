using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// Compliance's copies of Organisation's memberships, their changes and their endings; implemented in its Infrastructure
/// project. What is added is saved when the module's unit of work commits. The consumers never change what they stored:
/// a change of job function or role is a row of its own, one per version, and the current state is resolved when it is
/// read (docs/adr/0015). A later erasure (DSGVO) would be a deliberate new path, not a breach of this store.
/// </summary>
public interface IMemberStore
{
    Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(Member member);

    Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(MembershipEnding ending);

    /// <summary>Whether the change of this membership at this version is stored.</summary>
    Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken);

    void Add(MembershipChange change);

    /// <summary>The person's memberships in the pharmacy that have not ended, each as it stands now with its changes.</summary>
    Task<IReadOnlyList<CurrentMembership>> ActiveMembershipsAsync(PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>Every membership in the pharmacy that has not ended, each as it stands now with its changes.</summary>
    Task<IReadOnlyList<CurrentMembership>> ActiveMembershipsInPharmacyAsync(PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>Which of the persons have a membership in the pharmacy that has not ended.</summary>
    Task<IReadOnlySet<PersonId>> ActiveMembersAsync(PharmacyId pharmacy, IReadOnlyCollection<PersonId> persons, CancellationToken cancellationToken);
}
