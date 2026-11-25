using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>The Certification module's unit of work; implemented in its Infrastructure project.</summary>
public interface ICertificationUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Gives up the tracked changes since the last commit: rolls back the open transaction, which ends a person's hold
    /// (#115), and forgets every tracked change, so the scope can go on with the next person in a transaction of its own.
    /// It does nothing when nothing is open. It does not take back an event already handed to <see cref="IUnitOfWork.PublishAsync"/>:
    /// the outbox keeps it for the next commit, so a use case that publishes must not abandon and then commit. Every unit
    /// of work declares it, so none can leave a person's transaction open for the next one by default.
    /// </summary>
    Task AbandonAsync(CancellationToken cancellationToken);
}
