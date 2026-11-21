using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// Compliance's copies of Organisation's memberships and their endings; implemented in its Infrastructure project. What
/// is added is saved when the module's unit of work commits. The consumers never change what they stored; a later
/// erasure (DSGVO) or a change event would be a deliberate new path, not a breach of this store.
/// </summary>
public interface IMemberStore
{
    Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(Member member);

    Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(MembershipEnding ending);
}
