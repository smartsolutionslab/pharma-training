using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>The Organisation module's unit of work; implemented in its Infrastructure project.</summary>
public interface IOrganisationUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Saves like <see cref="IUnitOfWork.CommitAsync"/>, but answers <see langword="false"/>, having saved and sent
    /// nothing, when a pharmacy it changes was changed by another save since it was loaded. The module's unit of work
    /// implements it over the pharmacy's row version; a unit of work that cannot lose a race keeps this one.
    /// </summary>
    async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        await CommitAsync(cancellationToken);
        return true;
    }
}
