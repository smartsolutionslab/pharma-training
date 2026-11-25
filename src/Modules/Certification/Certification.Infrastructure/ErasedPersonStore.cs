using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The erased persons' tombstones in Certification (#115). The module's unit of work saves what is added; an erasure and
/// a membership, passed-test or document message of the person run one after the other under the hold, and a second
/// delivery of the erasure waits for the first and finds its tombstone.
/// </summary>
public sealed class ErasedPersonStore(CertificationDbContext context) : IErasedPersonStore
{
    private readonly CertificationDbContext context = context;

    /// <summary>
    /// A transaction-scoped advisory lock keyed on the person, taken in the context's transaction (begun here when none is
    /// open; the unit of work commits it). It ends with the transaction, committed or rolled back.
    /// </summary>
    public async Task HoldAsync(PersonId person, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();

        if (context.Database.CurrentTransaction is null)
        {
            await context.Database.BeginTransactionAsync(cancellationToken);
        }

        var key = $"certification.person:{person.Value:D}";
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) =>
        context.Set<ErasedPerson>().AnyAsync(erased => erased.Person == person, cancellationToken);

    public void Add(ErasedPerson erased) => context.Add(erased);

    public Task<IReadOnlyList<PersonId>> HoldingCertificatesAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException("Reading the erased persons who still hold certificates is not implemented yet (#115, slice 5).");
}
