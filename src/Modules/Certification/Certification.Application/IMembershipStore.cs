using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// Certification's copies of Organisation's memberships, their endings and their changes; implemented in its Infrastructure project.
/// What is added is saved when the module's unit of work commits; the consumers never change what they stored.
/// </summary>
public interface IMembershipStore
{
    Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(Membership membership);

    Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken);

    void Add(MembershipEnding ending);

    /// <summary>Whether a change of the membership at the version is stored.</summary>
    Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken);

    void Add(MembershipChange change);

    /// <summary>
    /// The current role of each of the person's memberships in the pharmacy that has not ended (docs/adr/0015); none
    /// while the person has no such membership there.
    /// </summary>
    Task<IReadOnlyList<MembershipRole>> ActiveRolesAsync(PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>
    /// The name of the person's membership that started last, ended or not, and of the one with the highest id where two
    /// started at once; none while no membership of the person has arrived.
    /// </summary>
    Task<Option<HolderName>> FindHolderNameAsync(PersonId person, CancellationToken cancellationToken);
}
