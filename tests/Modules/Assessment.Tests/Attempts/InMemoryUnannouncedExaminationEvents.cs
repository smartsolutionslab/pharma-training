using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The stored events of one type without a marker (#307), as the event store would find them, oldest first, at most as
/// many as asked for, each in its examination's stream. Every question is recorded, so a test sees the window and the
/// limit a pass asked for. Each is at its place in <see cref="Stored"/> in the event store's sequence, counted from 1. One
/// in <see cref="Unreadable"/> is found but throws when it is loaded, as an event Marten can no longer read would; one in
/// <see cref="Unreachable"/> throws as a database that is gone would.
/// </summary>
internal sealed class InMemoryUnannouncedExaminationEvents<TEvent> : IUnannouncedExaminationEvents<TEvent>
    where TEvent : class, IDomainEvent
{
    private readonly Dictionary<Guid, PlantedExaminationEvent<TEvent>> loaded = [];

    public List<PlantedExaminationEvent<TEvent>> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public HashSet<PlantedExaminationEvent<TEvent>> Unreadable { get; } = [];

    public HashSet<PlantedExaminationEvent<TEvent>> Unreachable { get; } = [];

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, after, limit));
        IReadOnlyList<UnannouncedEvent> found = [.. Stored.Select((stored, index) => Found(stored, index + 1L)).Where(@event => @event.Sequence > after).Take(limit)];
        return Task.FromResult(found);
    }

    public Task<StoredExaminationEvent<TEvent>> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        var stored = loaded[found.EventId];
        if (Unreachable.Contains(stored))
        {
            throw new DatabaseUnavailable();
        }

        if (Unreadable.Contains(stored))
        {
            throw new JsonException($"The stored event does not match {typeof(TEvent).Name}.");
        }

        return Task.FromResult(new StoredExaminationEvent<TEvent>(stored.Event, stored.Examination));
    }

    private UnannouncedEvent Found(PlantedExaminationEvent<TEvent> stored, long sequence)
    {
        var id = Guid.CreateVersion7();
        loaded[id] = stored;
        return new UnannouncedEvent(id, stored.Examination.Id.Value, stored.StreamVersion, stored.StoredAt, sequence);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, long After, int Limit);
}
