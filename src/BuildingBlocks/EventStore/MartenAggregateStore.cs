using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using Marten;

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

    public async Task SaveAsync(IEventSourced aggregate, CancellationToken cancellationToken)
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
