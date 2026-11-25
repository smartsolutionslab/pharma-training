using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;

/// <summary>
/// Loads a stored event of an examination for an announcement sweep (#307) with its examination: the examination's stream
/// up to the event's version, read in one query and replayed as the aggregate store replays it, so the examination is as
/// the stream held it once the event was stored. A database failure Marten wraps once its command runs arrives as the
/// <see cref="System.Data.Common.DbException"/> it wraps (<see cref="MartenDatabaseFailures"/>, #337), so the sweep ends
/// the pass on it instead of skipping the event as unreadable.
/// </summary>
internal static class StoredExaminationEvents
{
    public static Task<StoredExaminationEvent<TEvent>> LoadAsync<TEvent>(IDocumentStore store, UnannouncedEvent found, CancellationToken cancellationToken)
        where TEvent : class, IDomainEvent
    {
        Ensure.That(store).IsNotNull();
        Ensure.That(found).IsNotNull();

        return MartenDatabaseFailures.ReadAsync(async () =>
        {
            await using var session = store.QuerySession();
            var events = await session.Events.FetchStreamAsync(found.StreamId, version: found.StreamVersion, token: cancellationToken);
            var stored = events.Count > 0 && events[^1].Id == found.EventId
                ? events[^1]
                : throw new InvalidOperationException($"Assessment's event store holds no event {found.EventId} at version {found.StreamVersion} of stream {found.StreamId}.");

            // Aggregates keep their parameterless constructor private: only loading may create an empty one.
            var examination = (Examination)Activator.CreateInstance(typeof(Examination), nonPublic: true)!;
            ((IEventSourced)examination).Replay(events.Select(@event => (IDomainEvent)@event.Data));
            return new StoredExaminationEvent<TEvent>((TEvent)stored.Data, examination);
        });
    }
}
