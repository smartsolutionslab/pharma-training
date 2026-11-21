using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// Compliance's copies of Organisation's memberships and their endings; implemented in its Infrastructure project. What
/// is added is saved when the module's unit of work commits, and nothing stored is ever changed.
/// </summary>
public interface IMemberStore
{
    Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(Member member);

    Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(MembershipEnding ending);
}
