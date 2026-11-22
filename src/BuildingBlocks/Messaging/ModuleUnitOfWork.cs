using SmartSolutionsLab.PharmaTraining.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// A module's unit of work over Wolverine's EF Core outbox. The outbox is the host's (messaging is host-wide), so it
/// is resolved on first use; a module's own registrations stay valid without a running host.
/// </summary>
public abstract class ModuleUnitOfWork<TContext>(IServiceProvider services) : IUnitOfWork
    where TContext : DbContext
{
    private IDbContextOutbox<TContext>? outbox;

    private IDbContextOutbox<TContext> Outbox => outbox ??= services.GetRequiredService<IDbContextOutbox<TContext>>();

    public async Task PublishAsync(IntegrationEvent integrationEvent)
    {
        Ensure.That(integrationEvent).IsNotNull();
        await Outbox.PublishAsync(IntegrationEventMetadata.Fill(integrationEvent, services));
    }

    public Task CommitAsync(CancellationToken cancellationToken) => Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

    /// <summary>
    /// <see cref="CommitAsync"/> for a save that can lose a race: <see langword="false"/> when the row version of an
    /// aggregate it changes (<c>RowVersion</c>) moved on since it was loaded. Nothing is saved or sent then: the save's
    /// transaction, with the outbox's envelopes, rolls back, and the outbox flushes only after a save that succeeded.
    /// A module opts in by declaring this method on its own unit of work's interface; <see cref="CommitAsync"/> still
    /// lets a lost race through, as a 500.
    /// A deadlock (40P01) or serialization failure (40001) that PostgreSQL raises at the commit is a lost race too.
    /// EF Core wraps it in <see cref="DbUpdateConcurrencyException"/> when it owns the transaction, but once a handler
    /// publishes, Wolverine's envelope transaction commits through <c>Database.CommitTransactionAsync</c> and the
    /// failure arrives as a bare <see cref="PostgresException"/>, or inside another exception. Every other failure is
    /// rethrown as it came.
    /// </summary>
    public async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (Exception exception) when (EndedAsLostRace(exception))
        {
            return false;
        }
    }

    private static bool EndedAsLostRace(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure }
        || (exception.InnerException is { } inner && EndedAsLostRace(inner));
}
