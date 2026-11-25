using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Learning's copy of the memberships, their changes and their endings in memory, and its unit of work over them: what
/// is added is stored only once committed. Copied from Compliance's <c>InMemoryMemberStore</c> and
/// <see cref="InMemoryCourseVersionCopies"/>; the <c>Hold</c> helpers store a row as if its event had been handled.
/// </summary>
internal sealed class InMemoryMembershipCopies : IMembershipCopies, ILearningUnitOfWork
{
    private readonly List<MemberCopy> addedMembers = [];
    private readonly List<MembershipEndingCopy> addedEndings = [];
    private readonly List<MembershipChangeCopy> addedChanges = [];
    private readonly List<IntegrationEvent> pending = [];

    public List<MemberCopy> Members { get; } = [];

    public List<MembershipEndingCopy> Endings { get; } = [];

    public List<MembershipChangeCopy> Changes { get; } = [];

    public int Commits { get; private set; }

    /// <summary>The integration events published through the unit of work and committed with it.</summary>
    public List<IntegrationEvent> Published { get; } = [];

    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Members.Any(member => member.Id == membership.Value));

    public void Add(MemberCopy member) => addedMembers.Add(member);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        Task.FromResult(Endings.Any(ending => ending.Id == membership.Value));

    public void Add(MembershipEndingCopy ending) => addedEndings.Add(ending);

    public Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken) =>
        Task.FromResult(Changes.Any(change => change.MembershipId == membership.Value && change.Version == version.Value));

    public void Add(MembershipChangeCopy change) => addedChanges.Add(change);

    public Task<PersonMemberships> OfPersonAsync(PersonId person, CancellationToken cancellationToken) =>
        Task.FromResult(new PersonMemberships(
            person,
            [.. Members.Where(member => member.PersonId == person.Value)],
            [.. Changes.Where(change => change.PersonId == person.Value)],
            [.. Endings.Where(ending => ending.PersonId == person.Value)]));

    public void Hold(MemberCopy member) => Members.Add(member);

    public void Hold(MembershipChangeCopy change) => Changes.Add(change);

    public void Hold(MembershipEndingCopy ending) => Endings.Add(ending);

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        pending.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Members.AddRange(addedMembers);
        addedMembers.Clear();
        Endings.AddRange(addedEndings);
        addedEndings.Clear();
        Changes.AddRange(addedChanges);
        addedChanges.Clear();
        Published.AddRange(pending);
        pending.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
