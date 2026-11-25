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

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) =>
        context.Set<ErasedPerson>().AnyAsync(erased => erased.Person == person, cancellationToken);

    public void Add(ErasedPerson erased) => context.Add(erased);
}
