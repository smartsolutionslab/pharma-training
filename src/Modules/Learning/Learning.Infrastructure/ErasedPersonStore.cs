using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure;

/// <summary>
/// The erased persons' tombstones in Learning (#115). Not implemented yet, the #421 review's red tests: it holds no one,
/// finds no one erased and keeps nothing, so Learning behaves as before.
/// </summary>
public sealed class ErasedPersonStore(LearningDbContext context) : IErasedPersonStore
{
    private readonly LearningDbContext context = context;

    public Task HoldAsync(PersonId person, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) => Task.FromResult(context is null);

    public void Add(ErasedPersonCopy erased)
    {
    }
}
