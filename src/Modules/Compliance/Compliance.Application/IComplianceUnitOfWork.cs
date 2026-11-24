using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The Compliance module's unit of work; implemented in its Infrastructure project.</summary>
public interface IComplianceUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Saves like <see cref="IUnitOfWork.CommitAsync"/>, but answers how it lost a race (<see cref="LostRace"/>), having saved and sent
    /// nothing, when the save lost a race to another: a row it changes was changed since it was loaded, or it deadlocked
    /// with another save. The module's unit of work implements it; a unit of work that cannot lose a race keeps this one.
    /// </summary>
    async Task<Result<Unit, LostRace>> TryCommitAsync(CancellationToken cancellationToken)
    {
        await CommitAsync(cancellationToken);
        return Success(Unit.Value);
    }
}
