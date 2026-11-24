using SmartSolutionsLab.PharmaTraining.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Wolverine.Runtime;
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

    /// <summary>
    /// Saves and flushes with Wolverine's default, <see cref="MultiFlushMode.OnlyOnce"/>: a scope's outbox flushes once,
    /// and what a later commit in the same scope stores is sent by a durability pass, not at once (Wolverine logs a
    /// warning). The envelopes of a save that lost its race are not cleared: they stay in the context, outstanding, so a
    /// later plain commit in the scope does not send them at once, but a later <see cref="CommitOneOfSeveralAsync"/>,
    /// whose flush is <see cref="MultiFlushMode.AllowMultiples"/>, would send them with its own. A scope that may lose a
    /// race therefore never commits as one of several.
    /// </summary>
    public Task CommitAsync(CancellationToken cancellationToken) => Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

    /// <summary>
    /// <see cref="CommitAsync"/> for a use case that commits several times in one scope and must send each commit's
    /// events at once: the flush of this call alone is <see cref="MultiFlushMode.AllowMultiples"/>, so it sends exactly
    /// the envelopes stored since the scope's last flush, and the scope keeps <see cref="MultiFlushMode.OnlyOnce"/> for
    /// every other commit. Only a save that cannot lose a race may use it (nothing tracked changes), since the envelopes of
    /// a failed attempt would go out with the next one. A module offers it by declaring it on its own unit of work's
    /// interface for the one use case that needs it; today that is Editorial's republish (ADR 0019).
    /// </summary>
    public Task CommitOneOfSeveralAsync(CancellationToken cancellationToken) =>
        Outbox.SaveChangesAndFlushMessagesAsync(MultiFlushMode.AllowMultiples, cancellationToken);

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
    /// the telemetry, not only as 409s; a row version that moved on is ordinary and is not logged. Every other failure
    /// in the exception's tree (followed through each <see cref="Exception.InnerException"/> and every
    /// <see cref="AggregateException"/>'s inner exceptions, at any depth) is logged once as a warning of its own, with it
    /// attached, so it is not lost with the save answered as a 409; no wrapper or aggregate is logged by itself.
    /// Only a failure before the commit completed is classified: the save and the commit of any open transaction run first,
    /// as Wolverine's <c>SaveChangesAndFlushMessagesAsync</c> runs them (Wolverine 6.64.0), and Wolverine's flush after
    /// the commit (<c>EfCoreOutboxFlushing</c>) runs outside the classification, so whatever it throws, a 40P01 or 40001 of
    /// <c>DurabilityMode.ServerlessWithOutbox</c>'s delete of the sent outbox rows included, comes through as it came and
    /// never answers 409 for a change that is committed. Where domain-event scrapers are registered (none are here), it
    /// calls Wolverine's own <c>SaveChangesAndFlushMessagesAsync</c>, which runs them, and classifies its failures as a
    /// whole. Every other failure is rethrown as it came.
    /// </summary>
    public async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        var scraped = services.GetServices<IDomainEventScraper>().Any();
        try
        {
            await (scraped ? Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken) : SaveAndCommitAsync(cancellationToken));
        }
        catch (Exception exception) when (EndedAsLostRace(exception) is { } ended)
        {
            LogLostRace(Logger, ended, ended.SqlState, typeof(TContext).Name);
            foreach (var other in Leaves(exception).Distinct<Exception>(ReferenceEqualityComparer.Instance).Where(leaf => !ReferenceEquals(leaf, ended)))
            {
                LogOtherFailure(Logger, other, typeof(TContext).Name);
            }

            return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        if (!scraped)
        {
            // After the commit: the change is saved, so nothing here is a lost race.
            await (Outbox is MessageContext context ? EfCoreOutboxFlushing.FlushOutgoingMessagesAsync(context) : Outbox.FlushOutgoingMessagesAsync());
        }

        return true;
    }

    /// <summary>The steps of Wolverine's <c>SaveChangesAndFlushMessagesAsync</c> up to the commit: the save, then the commit of any open transaction.</summary>
    private async Task SaveAndCommitAsync(CancellationToken cancellationToken)
    {
        var context = Outbox.DbContext;
        await context.SaveChangesAsync(cancellationToken);
        if (context.Database.CurrentTransaction is not null)
        {
            await context.Database.CommitTransactionAsync(cancellationToken);
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

    /// <summary>
    /// The failures at the ends of the exception's tree: each wrapper is followed through its
    /// <see cref="Exception.InnerException"/>, each <see cref="AggregateException"/> through all its inner exceptions, at
    /// any depth, and neither a wrapper nor an aggregate is a failure of its own. A lost race is a leaf, whatever it wraps.
    /// </summary>
    private static IEnumerable<Exception> Leaves(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure } => [exception],
        AggregateException aggregate => aggregate.InnerExceptions.SelectMany(Leaves),
        { InnerException: { } inner } => Leaves(inner),
        _ => [exception],
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "PostgreSQL ended a save of {Context} with SqlState {SqlState}; answered as a lost race.")]
    private static partial void LogLostRace(ILogger logger, Exception exception, string sqlState, string context);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A save of {Context} that lost a race also failed otherwise; the save was answered as a lost race.")]
    private static partial void LogOtherFailure(ILogger logger, Exception exception, string context);
}
