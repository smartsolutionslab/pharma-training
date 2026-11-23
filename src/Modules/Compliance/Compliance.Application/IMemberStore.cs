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
}
