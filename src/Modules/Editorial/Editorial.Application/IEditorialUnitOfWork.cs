using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The Editorial module's unit of work; implemented in its Infrastructure project.</summary>
public interface IEditorialUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Saves like <see cref="IUnitOfWork.CommitAsync"/>, but answers how it lost a race (<see cref="LostRace"/>), having saved and sent
    /// nothing, when a course version it changes was changed by another save since it was loaded. The module's unit
    /// of work implements it over the version's row version; a unit of work that cannot lose a race keeps this one.
    /// </summary>
    async Task<Result<Unit, LostRace>> TryCommitAsync(CancellationToken cancellationToken)
    {
        await CommitAsync(cancellationToken);
        return Success(Unit.Value);
    }

    /// <summary>
    /// Saves like <see cref="IUnitOfWork.CommitAsync"/> and sends at once exactly the events published since the scope's
    /// last commit, for the one use case that commits several times in a scope: the republish (ADR 0019). Nothing it
    /// saves can lose a race. Every other use case commits with <see cref="IUnitOfWork.CommitAsync"/> or
    /// <see cref="TryCommitAsync"/>, which flush a scope once.
    /// </summary>
    Task CommitOneOfSeveralAsync(CancellationToken cancellationToken);
}
