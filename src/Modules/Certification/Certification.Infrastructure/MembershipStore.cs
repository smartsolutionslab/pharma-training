using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The memberships and their endings. The module's unit of work saves what is added; nothing is ever updated, so two
/// deliveries of one event race only on its primary key.
/// </summary>
public sealed class MembershipStore(CertificationDbContext context) : IMembershipStore
{
    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        context.Set<Membership>().AnyAsync(stored => stored.Id == membership, cancellationToken);

    public void Add(Membership membership) => context.Add(membership);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        context.Set<MembershipEnding>().AnyAsync(ending => ending.Id == membership, cancellationToken);

    public void Add(MembershipEnding ending) => context.Add(ending);

    public async Task<Option<HolderName>> FindHolderNameAsync(PersonId person, CancellationToken cancellationToken) =>
        await context.Set<Membership>()
            .AsNoTracking()
            .Where(membership => membership.Person == person)
            .OrderByDescending(membership => membership.StartedAt)
            .ThenByDescending(membership => membership.Id)
            .Select(membership => membership.HolderName)
            .FirstOrDefaultAsync(cancellationToken) is { } name
            ? Option<HolderName>.Some(name)
            : Option<HolderName>.None;
}
