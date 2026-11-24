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

    private IDbContextOutbox<TContext> Outbox => outbox ??= Resolve(services);

    private ILogger Logger => logger ??= services.GetService<ILogger<ModuleUnitOfWork<TContext>>>() ?? NullLogger<ModuleUnitOfWork<TContext>>.Instance;

    public async Task PublishAsync(IntegrationEvent integrationEvent)
    {
        Ensure.That(integrationEvent).IsNotNull();
        await Outbox.PublishAsync(IntegrationEventMetadata.Fill(integrationEvent, services));
    }

    public Task CommitAsync(CancellationToken cancellationToken) => Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

    /// <summary>
    /// <see cref="CommitAsync"/> for a save that can lose a race, answering how it lost (<see cref="LostRace"/>):
    /// <see cref="VersionMovedOn"/> when the row version of an aggregate it changes (<c>RowVersion</c>) moved on since it
    /// was loaded, and <see cref="SaveDeadlocked"/> when PostgreSQL ended it as a deadlock or a serialization failure
    /// (below), which a module answers apart: the first means load again, the second try again. Nothing is saved or sent then: the save's
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
    /// as Wolverine's <c>SaveChangesAndFlushMessagesAsync</c> runs them (Wolverine 6.64.0, which
    /// <c>WolverineVersionTests</c> pins: recheck that method when it changes), and Wolverine's flush after
    /// the commit (<c>EfCoreOutboxFlushing</c>) runs outside the classification, so whatever it throws, a 40P01 or 40001 of
    /// <c>DurabilityMode.ServerlessWithOutbox</c>'s delete of the sent outbox rows included, comes through as it came and
    /// never answers 409 for a change that is committed. Registered domain-event scrapers run first, inside the
    /// classification, as Wolverine's method runs them, so no path classifies a failure after the commit. Every other failure is rethrown as it came.
    /// </summary>
    public async Task<Result<Unit, LostRace>> TryCommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ScrapeSaveAndCommitAsync(cancellationToken);
        }
        catch (Exception exception) when (EndedAsLostRace(exception) is { } ended)
        {
            LogLostRace(Logger, ended, ended.SqlState, typeof(TContext).Name);
            foreach (var other in Leaves(exception).Distinct<Exception>(ReferenceEqualityComparer.Instance).Where(leaf => !ReferenceEquals(leaf, ended)))
            {
                LogOtherFailure(Logger, other, typeof(TContext).Name);
            }

            return Failure(LostRaces.Deadlocked(ended.SqlState));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(LostRaces.VersionMovedOn());
        }

        // After the commit: the change is saved, so nothing here is a lost race, whatever the flush throws. Every outbox
        // the host resolves is a MessageContext; only a test's substitute takes the interface's own flush.
        await (Outbox is MessageContext context ? EfCoreOutboxFlushing.FlushOutgoingMessagesAsync(context) : Outbox.FlushOutgoingMessagesAsync());
        return Success(Unit.Value);
    }

    /// <summary>
    /// The steps of Wolverine's <c>SaveChangesAndFlushMessagesAsync</c> up to the commit: every registered domain-event
    /// scraper (none is registered here), the save, then the commit of any open transaction. The flush after it is the
    /// caller's, outside the lost-race classification.
    /// </summary>
    private async Task ScrapeSaveAndCommitAsync(CancellationToken cancellationToken)
    {
        var context = Outbox.DbContext;
        var scrapers = services.GetServices<IDomainEventScraper>().ToList();
        if (scrapers.Count > 0)
        {
            var bus = Outbox as MessageContext
                ?? throw new InvalidOperationException("A domain-event scraper needs Wolverine's message context, which every outbox the host resolves is.");
            foreach (var scraper in scrapers)
            {
                await scraper.ScrapeEvents(context, bus);
            }
        }

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

    /// <summary>
    /// The scope's outbox, allowed to flush after every commit. Wolverine's default flushes a message context once and
    /// keeps what a later commit stores in the outbox unsent (with a warning) until a durability pass finds it, so a use
    /// case that commits more than once in a scope (a republish, one commit per course version) would send only its first
    /// commit's events at once. Each flush sends only the envelopes stored since the last one.
    /// </summary>
    private static IDbContextOutbox<TContext> Resolve(IServiceProvider services)
    {
        var resolved = services.GetRequiredService<IDbContextOutbox<TContext>>();
        if (resolved is MessageContext context)
        {
            context.MultiFlushMode = MultiFlushMode.AllowMultiples;
        }

        return resolved;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PostgreSQL ended a save of {Context} with SqlState {SqlState}; answered as a lost race.")]
    private static partial void LogLostRace(ILogger logger, Exception exception, string sqlState, string context);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A save of {Context} that lost a race also failed otherwise; the save was answered as a lost race.")]
    private static partial void LogOtherFailure(ILogger logger, Exception exception, string context);
}
