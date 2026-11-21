using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The pharmacies table in memory, and the module's unit of work over it: what is added is stored, and what is
/// published is sent, only once committed.
/// </summary>
internal sealed class InMemoryPharmacyStore : IPharmacyStore, IOrganisationUnitOfWork
{
    private readonly List<Pharmacy> added = [];
    private readonly List<IntegrationEvent> queued = [];

    public List<Pharmacy> Stored { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    public void Add(Pharmacy pharmacy) => added.Add(pharmacy);

    public Task<Option<Pharmacy>> FindAsync(PharmacyId id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(pharmacy => pharmacy.Id == id) is { } pharmacy ? Option<Pharmacy>.Some(pharmacy) : Option<Pharmacy>.None);

    /// <summary>The stored pharmacy itself, so what a command changes on it is what the next read finds once committed.</summary>
    public Task<Option<Pharmacy>> LoadAsync(PharmacyId id, CancellationToken cancellationToken) => FindAsync(id, cancellationToken);

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
        Stored.AddRange(added);
        added.Clear();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
