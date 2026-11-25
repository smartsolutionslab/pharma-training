using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// The memberships, their endings and their changes in memory, and the module's unit of work over them: what is added is
/// stored only once committed. The holder's name is read as the store in Infrastructure reads it: the name of the
/// person's membership that started last, ended or not, and of the one with the highest id where two started at once.
/// A membership is active while no ending of it is stored; its current role is the one its newest change gives it.
/// </summary>
internal sealed class InMemoryMembershipStore : IMembershipStore, ICertificationUnitOfWork
{
    private readonly List<Membership> addedMemberships = [];
    private readonly List<MembershipEnding> addedEndings = [];
    private readonly List<MembershipChange> addedChanges = [];

    public List<Membership> Memberships { get; } = [];

    public List<MembershipEnding> Endings { get; } = [];

    public List<MembershipChange> Changes { get; } = [];

    public int Commits { get; private set; }

    /// <summary>Where the reads of a person's memberships are written down, in order with the rest of a test's (#115); none by default.</summary>
    public List<string>? Journal { get; set; }

    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Memberships.Any(stored => stored.Id == membership));

    public void Add(Membership membership) => addedMemberships.Add(membership);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Endings.Any(ending => ending.Id == membership));

    public void Add(MembershipEnding ending) => addedEndings.Add(ending);

    public Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken) =>
        Task.FromResult(Changes.Any(change => change.Membership == membership && change.Version == version));

    public void Add(MembershipChange change) => addedChanges.Add(change);

    public Task<IReadOnlyList<MembershipRole>> ActiveRolesAsync(PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        IReadOnlyList<MembershipRole> roles = Memberships
            .Where(membership => membership.Person == person && membership.Pharmacy == pharmacy)
            .Where(membership => !Endings.Any(ending => ending.Id == membership.Id))
            .Select(membership => membership.CurrentRole(Changes.Where(change => change.Membership == membership.Id).ToList()))
            .ToList();
        return Task.FromResult(roles);
    }

    public Task<Option<HolderName>> FindHolderNameAsync(PersonId person, CancellationToken cancellationToken) =>
        Task.FromResult(Memberships
            .Where(membership => membership.Person == person)
            .OrderByDescending(membership => membership.StartedAt.Value)
            .ThenByDescending(membership => membership.Id.Value)
            .FirstOrDefault() is { } latest
            ? Option<HolderName>.Some(latest.HolderName)
            : Option<HolderName>.None);

    public Task<IReadOnlyList<Membership>> OfPersonAsync(PersonId person, CancellationToken cancellationToken)
    {
        Journal?.Add("read-memberships");
        IReadOnlyList<Membership> persons = [.. Memberships.Where(membership => membership.Person == person)];
        return Task.FromResult(persons);
    }

    public Task<IReadOnlyList<MembershipEnding>> EndingsOfPersonAsync(PersonId person, CancellationToken cancellationToken)
    {
        Journal?.Add("read-endings");
        IReadOnlyList<MembershipEnding> persons = [.. Endings.Where(ending => ending.Person == person)];
        return Task.FromResult(persons);
    }

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Memberships.AddRange(addedMemberships);
        addedMemberships.Clear();
        Endings.AddRange(addedEndings);
        addedEndings.Clear();
        Changes.AddRange(addedChanges);
        addedChanges.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
