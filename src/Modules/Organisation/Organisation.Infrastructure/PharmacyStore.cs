using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence.Configurations;
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

        // Across every pharmacy: the link names none. The unique index on invitation_links.token_hash finds at most one.
        return await context.Set<Pharmacy>()
            .Include(pharmacy => pharmacy.Memberships)
            .SingleOrDefaultAsync(pharmacy => pharmacy.Memberships.Any(membership => membership.InvitationLinks.Any(link => link.Hash == hash)), cancellationToken) is { } pharmacy
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

    public async Task<PagedResult<Membership>> ListActiveMembershipsAsync(PharmacyId pharmacy, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var memberships = MembershipsOf(pharmacy).Where(membership => membership.Status == MembershipStatus.Active);
        var total = await memberships.CountAsync(cancellationToken);
        var items = await memberships
            .OrderBy(membership => membership.Name)
            .ThenBy(membership => membership.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<Membership>(items, total, page.Value, pageSize.Value);
    }

    public async Task<PagedResult<Membership>> ListInvitedMembershipsAsync(PharmacyId pharmacy, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        // Expired invitations stay Invited until someone renews or revokes them, so they are listed too, first.
        var memberships = MembershipsOf(pharmacy).Where(membership => membership.Status == MembershipStatus.Invited);
        var total = await memberships.CountAsync(cancellationToken);
        var items = await memberships
            .OrderBy(membership => membership.InvitationExpiresAt)
            .ThenBy(membership => membership.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<Membership>(items, total, page.Value, pageSize.Value);
    }

    /// <summary>The pharmacy's memberships, by the shadow key the Domain keeps in the aggregate; as stored, for a query.</summary>
    private IQueryable<Membership> MembershipsOf(PharmacyId pharmacy)
    {
        Ensure.That(pharmacy).IsNotNull();

        return context.Set<Membership>()
            .AsNoTracking()
            .Where(membership => EF.Property<PharmacyId>(membership, MembershipConfiguration.PharmacyId) == pharmacy);
    }

    private IQueryable<Pharmacy> WithMemberships() =>
        context.Set<Pharmacy>().AsNoTracking().Include(pharmacy => pharmacy.Memberships);
}
