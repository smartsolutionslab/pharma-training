using SmartSolutionsLab.PharmaTraining.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// A module's unit of work over Wolverine's EF Core outbox. The outbox is the host's (messaging is host-wide), so it
/// is resolved on first use; a module's own registrations stay valid without a running host.
/// </summary>
public abstract partial class ModuleUnitOfWork<TContext>(IServiceProvider services) : IUnitOfWork
    where TContext : DbContext
{
    private IDbContextOutbox<TContext>? outbox;

    private ILogger? logger;

    private IDbContextOutbox<TContext> Outbox => outbox ??= services.GetRequiredService<IDbContextOutbox<TContext>>();

    private ILogger Logger => logger ??= services.GetService<ILogger<ModuleUnitOfWork<TContext>>>() ?? NullLogger<ModuleUnitOfWork<TContext>>.Instance;

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
    /// A deadlock (40P01) or serialization failure (40001) that PostgreSQL raises anywhere in the save is a lost race too:
    /// at one of its statements, where EF Core wraps it in a plain <see cref="DbUpdateException"/>; at the commit EF Core
    /// owns, where it wraps it in <see cref="DbUpdateConcurrencyException"/>; and, once a handler publishes, at the commit
    /// of Wolverine's envelope transaction through <c>Database.CommitTransactionAsync</c>, where it arrives as a bare
    /// <see cref="PostgresException"/> or inside another exception, any inner exception of an
    /// <see cref="AggregateException"/> included. Each is logged as a warning naming its SqlState, with the
    /// <see cref="PostgresException"/> attached so its stack shows where the deadlock happened (Npgsql redacts
    /// PostgreSQL's Detail unless <c>Include Error Detail</c> is set, so keep it off; Where names the relation and
    /// tuple, never values), so a deadlock that keeps happening (two handlers taking locks in opposite orders) shows in
    /// the telemetry, not only as 409s; a row version that moved on is ordinary and is not logged. Every other inner
    /// exception of an <see cref="AggregateException"/> that holds the lost race is logged as a warning of its own,
    /// with it attached, so it is not lost with the aggregate answered as a 409. The filter also covers Wolverine's
    /// flush after the commit, but that flush catches each envelope's send failure itself (Wolverine 6.64.0, outside
    /// <c>DurabilityMode.ServerlessWithOutbox</c>, which deletes the sent envelopes' outbox rows after the commit with
    /// no such catch), so a 40P01 or 40001 cannot come from a write that already committed; recheck both when upgrading
    /// Wolverine or changing its durability mode. Every other failure is rethrown as it came.
    /// </summary>
    public async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (EndedAsLostRace(exception) is { } ended)
        {
            LogLostRace(Logger, ended, ended.SqlState, typeof(TContext).Name);
            if (exception is AggregateException aggregate)
            {
                foreach (var other in aggregate.InnerExceptions.Where(inner => !ReferenceEquals(EndedAsLostRace(inner), ended)))
                {
                    LogOtherFailure(Logger, other, typeof(TContext).Name);
                }
            }

            return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>
    /// The deadlock or serialization failure that ended the save, wherever it is wrapped: an <see cref="AggregateException"/>
    /// is searched through all its inner exceptions, since <see cref="Exception.InnerException"/> names only its first.
    /// </summary>
    private static PostgresException? EndedAsLostRace(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure } ended => ended,
        AggregateException aggregate => aggregate.InnerExceptions.Select(EndedAsLostRace).FirstOrDefault(ended => ended is not null),
        { InnerException: { } inner } => EndedAsLostRace(inner),
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "PostgreSQL ended a save of {Context} with SqlState {SqlState}; answered as a lost race.")]
    private static partial void LogLostRace(ILogger logger, Exception exception, string sqlState, string context);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A save of {Context} that lost a race also failed otherwise; the save was answered as a lost race.")]
    private static partial void LogOtherFailure(ILogger logger, Exception exception, string context);
}
