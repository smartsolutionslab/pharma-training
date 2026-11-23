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
}
