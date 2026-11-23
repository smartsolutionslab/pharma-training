using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The module's pharmacies with their memberships; implemented in its Infrastructure project. What a command adds is
/// saved when it commits the module's unit of work.
/// </summary>
public interface IPharmacyStore
{
    void Add(Pharmacy pharmacy);

    /// <summary>
    /// The pharmacy with its memberships as it is stored, for a query: nothing that changes it is saved. It is read
    /// afresh, never from what this unit of work already loaded, so it sees what other writers committed since.
    /// </summary>
    Task<Option<Pharmacy>> FindAsync(PharmacyId id, CancellationToken cancellationToken);

    /// <summary>The pharmacy with its memberships, for a command: what it changes is saved when the unit of work commits.</summary>
    Task<Option<Pharmacy>> LoadAsync(PharmacyId id, CancellationToken cancellationToken);

    /// <summary>
    /// The one pharmacy, of all, whose memberships hold the hash, with its memberships, for a command: what it changes
    /// is saved when the unit of work commits. Only an open invitation holds a hash.
    /// </summary>
    Task<Option<Pharmacy>> LoadByInvitationAsync(InvitationTokenHash hash, CancellationToken cancellationToken);

    /// <summary>One page of the pharmacies where the person is an active member, by name, with their memberships.</summary>
    Task<PagedResult<Pharmacy>> ListWithActiveMemberAsync(PersonId person, Page page, PageSize pageSize, CancellationToken cancellationToken);

    /// <summary>One page of the pharmacy's Active memberships, by name, then id, with how many it has; as stored, for a query.</summary>
    Task<PagedResult<Membership>> ListActiveMembershipsAsync(PharmacyId pharmacy, Page page, PageSize pageSize, CancellationToken cancellationToken);

    /// <summary>One page of the pharmacy's Invited memberships, open or run out, by expiry, then id, with how many it has; as stored, for a query.</summary>
    Task<PagedResult<Membership>> ListInvitedMembershipsAsync(PharmacyId pharmacy, Page page, PageSize pageSize, CancellationToken cancellationToken);
}
