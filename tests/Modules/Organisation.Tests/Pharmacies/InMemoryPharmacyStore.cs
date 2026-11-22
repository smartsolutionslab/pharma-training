using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The pharmacies table in memory, and the module's unit of work over it: what is added is stored, and what is
/// published is sent, only once committed. <see cref="LoseNextCommit"/> makes the next <see cref="TryCommitAsync"/>
/// lose a race, as a save does whose pharmacy another save changed since it was loaded; <see cref="FailNextCommit"/>
/// makes the next <see cref="CommitAsync"/> throw, as <c>CommitAsync</c> lets a lost race through.
/// </summary>
internal sealed class InMemoryPharmacyStore : IPharmacyStore, IOrganisationUnitOfWork
{
    private readonly List<Pharmacy> added = [];
    private readonly List<IntegrationEvent> queued = [];
    private bool loseNextCommit;
    private Exception? failNextCommit;
    private Action? onCommit;

    public List<Pharmacy> Stored { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    public void Add(Pharmacy pharmacy) => added.Add(pharmacy);

    public Task<Option<Pharmacy>> FindAsync(PharmacyId id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(pharmacy => pharmacy.Id == id) is { } pharmacy ? Option<Pharmacy>.Some(pharmacy) : Option<Pharmacy>.None);

    /// <summary>The stored pharmacy itself, so what a command changes on it is what the next read finds once committed.</summary>
    public Task<Option<Pharmacy>> LoadAsync(PharmacyId id, CancellationToken cancellationToken) => FindAsync(id, cancellationToken);

    /// <summary>The one stored pharmacy whose memberships hold the hash, as the database's unique index on it finds it.</summary>
    public Task<Option<Pharmacy>> LoadByInvitationAsync(InvitationTokenHash hash, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(pharmacy => pharmacy.Memberships.Any(membership => membership.TokenHash == hash)) is { } pharmacy
            ? Option<Pharmacy>.Some(pharmacy)
            : Option<Pharmacy>.None);

    public Task<PagedResult<Pharmacy>> ListWithActiveMemberAsync(PersonId person, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<Pharmacy> members = [.. Stored
            .Where(pharmacy => pharmacy.Memberships.Any(membership => membership.Person == person && membership.Status == MembershipStatus.Active))
            .OrderBy(pharmacy => pharmacy.Name.Value, StringComparer.Ordinal)
            .ThenBy(pharmacy => pharmacy.Id.Value)];
        List<Pharmacy> items = [.. members.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return Task.FromResult(new PagedResult<Pharmacy>(items, members.Count, page.Value, pageSize.Value));
    }

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        if (failNextCommit is { } failure)
        {
            failNextCommit = null;
            added.Clear();
            queued.Clear();
            throw failure;
        }

        Stored.AddRange(added);
        added.Clear();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        onCommit?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Runs the action as every <see cref="CommitAsync"/> completes, once it has stored and sent.</summary>
    public void OnCommit(Action action) => onCommit = action;

    /// <summary>The next <see cref="CommitAsync"/> throws the failure and stores and sends nothing.</summary>
    public void FailNextCommit(Exception failure) => failNextCommit = failure;

    /// <summary>The next <see cref="TryCommitAsync"/> answers <see langword="false"/> and stores and sends nothing.</summary>
    public void LoseNextCommit() => loseNextCommit = true;

    public async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        if (loseNextCommit)
        {
            loseNextCommit = false;
            added.Clear();
            queued.Clear();
            return false;
        }

        await CommitAsync(cancellationToken);
        return true;
    }
}
