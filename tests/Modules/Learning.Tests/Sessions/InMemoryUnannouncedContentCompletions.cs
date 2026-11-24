using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// The stored <c>ContentCompleted</c> events without a marker, as the event store would find them (#277), oldest first,
/// at most as many as asked for, each in its session's stream. Every question is recorded, so a test sees the window and
/// the limit a pass asked for.
/// </summary>
internal sealed class InMemoryUnannouncedContentCompletions : IUnannouncedContentCompletions
{
    private readonly Dictionary<Guid, ContentCompleted> loaded = [];

    public List<StoredContentCompletion> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, limit));
        IReadOnlyList<UnannouncedEvent> found = [.. Stored.Take(limit).Select(Found)];
        return Task.FromResult(found);
    }

    public Task<ContentCompleted> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken) =>
        Task.FromResult(loaded[found.EventId]);

    private UnannouncedEvent Found(StoredContentCompletion stored)
    {
        var id = Guid.CreateVersion7();
        loaded[id] = stored.Completed;
        return new UnannouncedEvent(id, stored.Completed.Session.Value, stored.StreamVersion, stored.StoredAt);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, int Limit);
}
