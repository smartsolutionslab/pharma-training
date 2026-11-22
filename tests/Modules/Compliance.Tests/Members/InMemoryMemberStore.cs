using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// The members and the endings of memberships in memory, and the module's unit of work over them: what is added is
/// stored only once committed.
/// </summary>
internal sealed class InMemoryMemberStore : IMemberStore, IComplianceUnitOfWork
{
    private readonly List<Member> addedMembers = [];
    private readonly List<MembershipEnding> addedEndings = [];

    public List<Member> Members { get; } = [];

    public List<MembershipEnding> Endings { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Members.Any(member => member.Id == membership));

    public void Add(Member member) => addedMembers.Add(member);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Endings.Any(ending => ending.Id == membership));

    public void Add(MembershipEnding ending) => addedEndings.Add(ending);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Members.AddRange(addedMembers);
        addedMembers.Clear();
        Endings.AddRange(addedEndings);
        addedEndings.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
