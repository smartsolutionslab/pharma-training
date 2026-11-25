using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>The Certification module's unit of work; implemented in its Infrastructure project.</summary>
public interface ICertificationUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Gives up what the scope did since its last commit: rolls back the open transaction, which ends a person's hold
    /// (#115), and forgets every tracked change, so the scope can go on with the next person in a transaction of its own.
    /// It does nothing when nothing is open; a unit of work that never opens a transaction keeps this one.
    /// </summary>
    Task AbandonAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
