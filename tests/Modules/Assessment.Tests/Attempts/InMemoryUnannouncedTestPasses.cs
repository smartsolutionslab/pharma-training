using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The stored <c>ExaminationPassed</c> events without a marker, as the event store would find them (#277), oldest first,
/// at most as many as asked for, each in its examination's stream. Every question is recorded, so a test sees the window
/// and the limit a pass asked for. Each is at its place in <see cref="Stored"/> in the event store's sequence, counted
/// from 1, and a page holds only those after the sequence it was asked to read after. One in <see cref="Unreadable"/> is found but throws when it is loaded, as an event
/// Marten can no longer read would.
/// </summary>
internal sealed class InMemoryUnannouncedTestPasses : IUnannouncedTestPasses
{
    private readonly Dictionary<Guid, StoredTestPass> loaded = [];

    public List<StoredTestPass> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public HashSet<StoredTestPass> Unreadable { get; } = [];

    /// <summary>Found, but the database is gone when it is loaded.</summary>
    public HashSet<StoredTestPass> Unreachable { get; } = [];

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, after, limit));
        IReadOnlyList<UnannouncedEvent> found = [.. Stored.Select((stored, index) => Found(stored, index + 1L)).Where(@event => @event.Sequence > after).Take(limit)];
        return Task.FromResult(found);
    }

    public Task<ExaminationPassed> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        var stored = loaded[found.EventId];
        if (Unreachable.Contains(stored))
        {
            throw new DatabaseUnavailable();
        }

        if (Unreadable.Contains(stored))
        {
            throw new JsonException($"The stored event at version {stored.StreamVersion} does not match ExaminationPassed.");
        }

        return Task.FromResult(stored.Passed);
    }

    private UnannouncedEvent Found(StoredTestPass stored, long sequence)
    {
        var id = Guid.CreateVersion7();
        loaded[id] = stored;
        return new UnannouncedEvent(id, stored.Stream ?? stored.Passed.Examination.Value, stored.StreamVersion, stored.StoredAt, sequence);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, long After, int Limit);
}
