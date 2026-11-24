using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>The Organisation module's unit of work; implemented in its Infrastructure project.</summary>
public interface IOrganisationUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Saves like <see cref="IUnitOfWork.CommitAsync"/>, but answers how it lost a race (<see cref="LostRace"/>), having saved and sent
    /// nothing, when an aggregate it changes (a pharmacy, an invitation recipient or sender) was changed by another save
    /// since it was loaded. The module's unit of work implements it over the aggregates' row versions; a unit of work
    /// that cannot lose a race keeps this one.
    /// </summary>
    async Task<Result<Unit, LostRace>> TryCommitAsync(CancellationToken cancellationToken)
    {
        await CommitAsync(cancellationToken);
        return Success(Unit.Value);
    }
}
