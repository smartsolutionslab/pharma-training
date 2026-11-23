using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// The memberships and their endings in memory, and the module's unit of work over them: what is added is stored only
/// once committed. The holder's name is read as the store in Infrastructure reads it: the name of the person's
/// membership that started last, ended or not, and of the one with the highest id where two started at once.
/// </summary>
internal sealed class InMemoryMembershipStore : IMembershipStore, ICertificationUnitOfWork
{
    private readonly List<Membership> addedMemberships = [];
    private readonly List<MembershipEnding> addedEndings = [];

    public List<Membership> Memberships { get; } = [];

    public List<MembershipEnding> Endings { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Memberships.Any(stored => stored.Id == membership));

    public void Add(Membership membership) => addedMemberships.Add(membership);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Endings.Any(ending => ending.Id == membership));

    public void Add(MembershipEnding ending) => addedEndings.Add(ending);

    public Task<Option<HolderName>> FindHolderNameAsync(PersonId person, CancellationToken cancellationToken) =>
        Task.FromResult(Memberships
            .Where(membership => membership.Person == person)
            .OrderByDescending(membership => membership.StartedAt.Value)
            .ThenByDescending(membership => membership.Id.Value)
            .FirstOrDefault() is { } latest
            ? Option<HolderName>.Some(latest.HolderName)
            : Option<HolderName>.None);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Memberships.AddRange(addedMemberships);
        addedMemberships.Clear();
        Endings.AddRange(addedEndings);
        addedEndings.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
