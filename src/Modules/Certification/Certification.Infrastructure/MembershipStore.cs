using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The memberships, their endings and their changes. The module's unit of work saves what is added; nothing is ever
/// updated, a change being a row of its own, so two deliveries of one event race only on its primary key.
/// </summary>
public sealed class MembershipStore(CertificationDbContext context) : IMembershipStore
{
    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken) =>
        context.Set<Membership>().AnyAsync(stored => stored.Id == membership, cancellationToken);

    public void Add(Membership membership) => context.Add(membership);

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken) =>
        context.Set<MembershipEnding>().AnyAsync(ending => ending.Id == membership, cancellationToken);

    public void Add(MembershipEnding ending) => context.Add(ending);

    public Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken) =>
        context.Set<MembershipChange>().AnyAsync(change => change.Membership == membership && change.Version == version, cancellationToken);

    public void Add(MembershipChange change) => context.Add(change);

    public async Task<IReadOnlyList<MembershipRole>> ActiveRolesAsync(PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        var active = context.Set<Membership>()
            .AsNoTracking()
            .Where(membership => membership.Person == person && membership.Pharmacy == pharmacy)
            .Where(membership => !context.Set<MembershipEnding>().Any(ending => ending.Id == membership.Id));
        var found = await active.ToListAsync(cancellationToken);
        var changes = await context.Set<MembershipChange>()
            .AsNoTracking()
            .Where(change => active.Any(membership => membership.Id == change.Membership))
            .ToListAsync(cancellationToken);
        return [.. found.Select(membership => membership.CurrentRole([.. changes.Where(change => change.Membership == membership.Id)]))];
    }

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

    // Not implemented yet: slice 4's red tests (#115).
    public Task<IReadOnlyList<Membership>> OfPersonAsync(PersonId person, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{person}");

    // Not implemented yet: slice 4's red tests (#115).
    public Task<IReadOnlyList<MembershipEnding>> EndingsOfPersonAsync(PersonId person, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{person}");
}
