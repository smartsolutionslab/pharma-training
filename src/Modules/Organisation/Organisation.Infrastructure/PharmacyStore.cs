using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>The pharmacies and their memberships. The module's unit of work saves what is added.</summary>
public sealed class PharmacyStore(OrganisationDbContext context) : IPharmacyStore
{
    public void Add(Pharmacy pharmacy) => context.Add(pharmacy);

    public async Task<Option<Pharmacy>> FindAsync(PharmacyId id, CancellationToken cancellationToken) =>
        await WithMemberships().SingleOrDefaultAsync(pharmacy => pharmacy.Id == id, cancellationToken) is { } pharmacy
            ? Option<Pharmacy>.Some(pharmacy)
            : Option<Pharmacy>.None;

    public async Task<Option<Pharmacy>> LoadAsync(PharmacyId id, CancellationToken cancellationToken) =>
        await context.Set<Pharmacy>().Include(pharmacy => pharmacy.Memberships).SingleOrDefaultAsync(pharmacy => pharmacy.Id == id, cancellationToken) is { } pharmacy
            ? Option<Pharmacy>.Some(pharmacy)
            : Option<Pharmacy>.None;

    public async Task<Option<Pharmacy>> LoadByInvitationAsync(InvitationTokenHash hash, CancellationToken cancellationToken)
    {
        Ensure.That(hash).IsNotNull();

        // Across every pharmacy: the link names none. The unique index on token_hash finds at most one.
        return await context.Set<Pharmacy>()
            .Include(pharmacy => pharmacy.Memberships)
            .SingleOrDefaultAsync(pharmacy => pharmacy.Memberships.Any(membership => membership.TokenHash == hash), cancellationToken) is { } pharmacy
            ? Option<Pharmacy>.Some(pharmacy)
            : Option<Pharmacy>.None;
    }

    public async Task<PagedResult<Pharmacy>> ListWithActiveMemberAsync(PersonId person, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var pharmacies = WithMemberships()
            .Where(pharmacy => pharmacy.Memberships.Any(membership => membership.Person == person && membership.Status == MembershipStatus.Active));
        var total = await pharmacies.CountAsync(cancellationToken);
        var items = await pharmacies
            .OrderBy(pharmacy => pharmacy.Name)
            .ThenBy(pharmacy => pharmacy.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<Pharmacy>(items, total, page.Value, pageSize.Value);
    }

    private IQueryable<Pharmacy> WithMemberships() =>
        context.Set<Pharmacy>().AsNoTracking().Include(pharmacy => pharmacy.Memberships);
}
