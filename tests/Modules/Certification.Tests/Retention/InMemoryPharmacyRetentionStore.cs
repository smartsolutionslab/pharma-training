using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

/// <summary>The pharmacies' retention periods in memory: a set replaces the pharmacy's period at once, as the upsert does.</summary>
internal sealed class InMemoryPharmacyRetentionStore : IPharmacyRetentionStore
{
    public Dictionary<PharmacyId, PharmacyRetention> Stored { get; } = [];

    public Task<Option<PharmacyRetention>> FindAsync(PharmacyId pharmacy, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.TryGetValue(pharmacy, out var stored)
            ? Option<PharmacyRetention>.Some(stored)
            : Option<PharmacyRetention>.None);

    public Task SetAsync(PharmacyRetention retention, CancellationToken cancellationToken)
    {
        Stored[retention.Id] = retention;
        return Task.CompletedTask;
    }

    /// <summary>Stores that the pharmacy keeps certificates for so many years.</summary>
    public void Keep(PharmacyId pharmacy, int years) =>
        Stored[pharmacy] = PharmacyRetention.Chosen(pharmacy, RetentionPeriod.From(years));
}
