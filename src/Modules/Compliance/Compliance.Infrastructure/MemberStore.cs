using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

/// <summary>
/// The members, the changes and the endings of memberships. The module's unit of work saves what is added; nothing is
/// ever updated, a change being a row of its own, so two deliveries of one event race only on its primary key.
/// </summary>
public sealed class MemberStore(ComplianceDbContext context) : IMemberStore
{
    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        context.Set<Member>().AnyAsync(member => member.Id == membership, cancellationToken);

    public void Add(Member member) => context.Add(member);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        context.Set<MembershipEnding>().AnyAsync(ending => ending.Id == membership, cancellationToken);

    public void Add(MembershipEnding ending) => context.Add(ending);

    public Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken) =>
        context.Set<MembershipChange>().AnyAsync(change => change.Membership == membership && change.Version == version, cancellationToken);

    public void Add(MembershipChange change) => context.Add(change);

    public Task<IReadOnlyList<CurrentMembership>> ActiveMembershipsAsync(PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken) =>
        CurrentAsync(Active().Where(member => member.Person == person && member.Pharmacy == pharmacy), cancellationToken);

    public Task<IReadOnlyList<CurrentMembership>> ActiveMembershipsInPharmacyAsync(PharmacyId pharmacy, CancellationToken cancellationToken) =>
        CurrentAsync(Active().Where(member => member.Pharmacy == pharmacy), cancellationToken);

    public async Task<IReadOnlySet<PersonId>> ActiveMembersAsync(PharmacyId pharmacy, IReadOnlyCollection<PersonId> persons, CancellationToken cancellationToken)
    {
        Ensure.That(persons).IsNotNull();

        List<PersonId> asked = [.. persons];
        var found = await Active()
            .Where(member => member.Pharmacy == pharmacy && asked.Contains(member.Person))
            .Select(member => member.Person)
            .ToListAsync(cancellationToken);
        return found.ToHashSet();
    }

    /// <summary>The members, each with its changes, as they stand now.</summary>
    private async Task<IReadOnlyList<CurrentMembership>> CurrentAsync(IQueryable<Member> active, CancellationToken cancellationToken)
    {
        var members = await active.ToListAsync(cancellationToken);
        var changes = await context.Set<MembershipChange>()
            .AsNoTracking()
            .Where(change => active.Any(member => member.Id == change.Membership))
            .ToListAsync(cancellationToken);
        return [.. members.Select(member => CurrentMembership.Of(member, [.. changes.Where(change => change.Membership == member.Id)]))];
    }

    /// <summary>The members whose membership has not ended, read without tracking.</summary>
    private IQueryable<Member> Active() =>
        context.Set<Member>()
            .AsNoTracking()
            .Where(member => !context.Set<MembershipEnding>().Any(ending => ending.Id == member.Id));
}
