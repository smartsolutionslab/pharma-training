using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The Editorial module's unit of work; implemented in its Infrastructure project.</summary>
public interface IEditorialUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Saves like <see cref="IUnitOfWork.CommitAsync"/>, but answers <see langword="false"/>, having saved and sent
    /// nothing, when a course version it changes was changed by another save since it was loaded. The module's unit
    /// of work implements it over the version's row version; a unit of work that cannot lose a race keeps this one.
    /// </summary>
    async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        await CommitAsync(cancellationToken);
        return true;
    }
}
