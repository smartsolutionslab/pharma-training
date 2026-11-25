using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>
/// The erased persons' tombstones in Editorial (#115). Not implemented yet, slice 3's red tests: it holds no one, finds
/// no one erased and keeps nothing, so Editorial behaves as before.
/// </summary>
public sealed class ErasedPersonStore(EditorialDbContext context) : IErasedPersonStore
{
    private readonly EditorialDbContext context = context;

    public Task HoldAsync(PersonId person, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) => Task.FromResult(context is null);

    public void Add(ErasedPerson erased)
    {
    }
}
