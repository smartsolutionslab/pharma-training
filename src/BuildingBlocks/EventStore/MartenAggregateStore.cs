using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using JasperFx.Events;
using Marten;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>A module's aggregate store over its Marten event store: one stream per aggregate, in the module's schema.</summary>
public abstract class MartenAggregateStore(IDocumentStore store) : IAggregateStore
{
    public async Task<Option<TAggregate>> LoadAsync<TAggregate>(IAggregateId id, CancellationToken cancellationToken)
        where TAggregate : class, IEventSourced
    {
        Ensure.That(id).IsNotNull();

        await using var session = store.QuerySession();
        var events = await session.Events.FetchStreamAsync(id.Value, token: cancellationToken);
        if (events.Count == 0)
        {
            return Option<TAggregate>.None;
        }

        // Aggregates keep their parameterless constructor private: only loading may create an empty one.
        var aggregate = (TAggregate)Activator.CreateInstance(typeof(TAggregate), nonPublic: true)!;
        aggregate.Replay(events.Select(stored => (IDomainEvent)stored.Data));
        return Option<TAggregate>.Some(aggregate);
    }

    public Task SaveAsync(IEventSourced aggregate, CancellationToken cancellationToken) => AppendAsync(aggregate, cancellationToken);

    /// <summary>
    /// <see cref="SaveAsync"/> for a save that can lose a race: <see langword="false"/> when another append landed on
    /// the stream since the aggregate was loaded. Nothing is stored then and the aggregate keeps its pending events.
    /// Marten's expected-version check refuses the append with <see cref="EventStreamUnexpectedMaxEventIdException"/>;
    /// a deadlock (40P01) or serialization failure (40001) PostgreSQL raises anywhere in the save, where the inline
    /// projections write in the same transaction, is a lost race too. A stream created twice
    /// (<c>ExistingStreamIdCollisionException</c>) is not: it stays the host's 409 already-exists (ADR 0010). A module
    /// opts in by declaring this method on its own aggregate store's interface; <see cref="SaveAsync"/> still lets a
    /// lost race through, as a 500 (ADR 0016).
    /// </summary>
    public async Task<bool> TrySaveAsync(IEventSourced aggregate, CancellationToken cancellationToken)
    {
        try
        {
            await AppendAsync(aggregate, cancellationToken);
            return true;
        }
        catch (Exception exception) when (EndedAsLostRace(exception))
        {
            return false;
        }
    }

    private static bool EndedAsLostRace(Exception exception) => exception switch
    {
        EventStreamUnexpectedMaxEventIdException => true,
        PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(EndedAsLostRace),
        { InnerException: { } inner } => EndedAsLostRace(inner),
        _ => false,
    };

    private async Task AppendAsync(IEventSourced aggregate, CancellationToken cancellationToken)
    {
        Ensure.That(aggregate).IsNotNull();

        object[] events = [.. aggregate.PendingEvents];
        if (events.Length == 0)
        {
            return;
        }

        await using var session = store.LightweightSession();
        if (aggregate.Version == 0)
        {
            session.Events.StartStream(aggregate.Id.Value, events);
        }
        else
        {
            // The version the stream must reach: Marten rejects the save if another one appended in between.
            session.Events.Append(aggregate.Id.Value, aggregate.Version + events.Length, events);
        }

        await session.SaveChangesAsync(cancellationToken);
        aggregate.MarkStored();
    }
}
