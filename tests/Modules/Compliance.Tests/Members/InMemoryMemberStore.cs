using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// The members, the endings and the changes of memberships in memory, and the module's unit of work over them: what is added is
/// stored only once committed.
/// </summary>
internal sealed class InMemoryMemberStore : IMemberStore, IComplianceUnitOfWork
{
    private readonly List<Member> addedMembers = [];
    private readonly List<MembershipEnding> addedEndings = [];
    private readonly List<MembershipChange> addedChanges = [];

    public List<Member> Members { get; } = [];

    public List<MembershipEnding> Endings { get; } = [];

    public List<MembershipChange> Changes { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Members.Any(member => member.Id == membership));

    public void Add(Member member) => addedMembers.Add(member);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Endings.Any(ending => ending.Id == membership));

    public void Add(MembershipEnding ending) => addedEndings.Add(ending);

    public Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken) =>
        Task.FromResult(Changes.Any(change => change.Membership == membership && change.Version == version));

    public void Add(MembershipChange change) => addedChanges.Add(change);

    public Task<IReadOnlyList<CurrentMembership>> ActiveMembershipsAsync(PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        IReadOnlyList<CurrentMembership> active =
        [
            .. Members
                .Where(member => member.Person == person && member.Pharmacy == pharmacy && !Endings.Any(ending => ending.Id == member.Id))
                .Select(member => CurrentMembership.Of(member, [.. Changes.Where(change => change.Membership == member.Id)])),
        ];
        return Task.FromResult(active);
    }

    public Task<IReadOnlySet<PersonId>> ActiveMembersAsync(PharmacyId pharmacy, IReadOnlyCollection<PersonId> persons, CancellationToken cancellationToken)
    {
        IReadOnlySet<PersonId> active = Members
            .Where(member => member.Pharmacy == pharmacy && persons.Contains(member.Person) && !Endings.Any(ending => ending.Id == member.Id))
            .Select(member => member.Person)
            .ToHashSet();
        return Task.FromResult(active);
    }

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Members.AddRange(addedMembers);
        addedMembers.Clear();
        Endings.AddRange(addedEndings);
        addedEndings.Clear();
        Changes.AddRange(addedChanges);
        addedChanges.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
