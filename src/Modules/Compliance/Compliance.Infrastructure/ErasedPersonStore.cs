using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

/// <summary>
/// The erased persons' tombstones. The module's unit of work saves what is added; two deliveries of one erasure race
/// only on the person's primary key, and the retry finds the tombstone.
/// </summary>
public sealed class ErasedPersonStore(ComplianceDbContext context) : IErasedPersonStore
{
    private readonly ComplianceDbContext context = context;

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

        var key = $"compliance.person:{person.Value:D}";
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) =>
        context.Set<ErasedPerson>().AnyAsync(erased => erased.Person == person, cancellationToken);

    public void Add(ErasedPerson erased) => context.Add(erased);
}
