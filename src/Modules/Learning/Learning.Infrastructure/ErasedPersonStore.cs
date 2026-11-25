using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure;

/// <summary>
/// The erased persons' tombstones in Learning (#115). The module's unit of work saves what is added; an erasure and a
/// membership message of the person run one after the other under the hold, and a second delivery of the erasure waits
/// for the first and finds its tombstone.
/// </summary>
public sealed class ErasedPersonStore(LearningDbContext context) : IErasedPersonStore
{
    private readonly LearningDbContext context = context;

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

        var key = $"learning.person:{person.Value:D}";
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();

        return context.Set<ErasedPersonCopy>().AnyAsync(erased => erased.PersonId == person.Value, cancellationToken);
    }

    public void Add(ErasedPersonCopy erased)
    {
        Ensure.That(erased).IsNotNull();

        context.Add(erased);
    }
}
