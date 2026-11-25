using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The retention period each pharmacy chose (#391, docs/adr/0039). A set is one <c>INSERT … ON CONFLICT DO UPDATE</c>:
/// two managers setting the period at once, also the pharmacy's first, never collide on the key; PostgreSQL serialises
/// them on the row and the later commit wins. Nothing is published, so the set needs no unit of work.
/// </summary>
public sealed class PharmacyRetentionStore(CertificationDbContext context) : IPharmacyRetentionStore
{
    public async Task<Option<PharmacyRetention>> FindAsync(PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        Ensure.That(pharmacy).IsNotNull();

        var stored = await context.Set<PharmacyRetention>()
            .AsNoTracking()
            .SingleOrDefaultAsync(retention => retention.Id == pharmacy, cancellationToken);
        if (stored is null)
        {
            return Option<PharmacyRetention>.None;
        }

        return Option<PharmacyRetention>.Some(stored);
    }

    public async Task SetAsync(PharmacyRetention retention, CancellationToken cancellationToken)
    {
        Ensure.That(retention).IsNotNull();

        var pharmacy = retention.Id.Value;
        var years = retention.Period.Value;
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO certification.pharmacy_retention_periods (pharmacy_id, years) VALUES ({pharmacy}, {years})
            ON CONFLICT (pharmacy_id) DO UPDATE SET years = excluded.years
            """,
            cancellationToken);
    }
}
