using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// The stored <c>ContentCompleted</c> events without a marker, as the event store would find them (#277), oldest first,
/// at most as many as asked for, each in its session's stream. Every question is recorded, so a test sees the window and
/// the limit a pass asked for. One in <see cref="Unreadable"/> is found but throws when it is loaded, as an event Marten
/// can no longer read would.
/// </summary>
internal sealed class InMemoryUnannouncedContentCompletions : IUnannouncedContentCompletions
{
    private readonly Dictionary<Guid, StoredContentCompletion> loaded = [];

    public List<StoredContentCompletion> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public HashSet<StoredContentCompletion> Unreadable { get; } = [];

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, limit));
        IReadOnlyList<UnannouncedEvent> found = [.. Stored.Take(limit).Select(Found)];
        return Task.FromResult(found);
    }

    public Task<ContentCompleted> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        var stored = loaded[found.EventId];
        if (Unreadable.Contains(stored))
        {
            throw new JsonException("The stored event does not match ContentCompleted.");
        }

        return Task.FromResult(stored.Completed);
    }

    private UnannouncedEvent Found(StoredContentCompletion stored)
    {
        var id = Guid.CreateVersion7();
        loaded[id] = stored;
        return new UnannouncedEvent(id, stored.Stream ?? stored.Completed.Session.Value, stored.StreamVersion, stored.StoredAt);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, int Limit);
}
