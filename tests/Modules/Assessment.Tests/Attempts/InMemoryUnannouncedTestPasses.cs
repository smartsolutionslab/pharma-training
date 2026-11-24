using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The stored <c>ExaminationPassed</c> events without a marker, as the event store would find them (#277), oldest first,
/// at most as many as asked for, each in its examination's stream. Every question is recorded, so a test sees the window
/// and the limit a pass asked for.
/// </summary>
internal sealed class InMemoryUnannouncedTestPasses : IUnannouncedTestPasses
{
    private readonly Dictionary<Guid, ExaminationPassed> loaded = [];

    public List<StoredTestPass> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, limit));
        IReadOnlyList<UnannouncedEvent> found = [.. Stored.Take(limit).Select(Found)];
        return Task.FromResult(found);
    }

    public Task<ExaminationPassed> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken) =>
        Task.FromResult(loaded[found.EventId]);

    private UnannouncedEvent Found(StoredTestPass stored)
    {
        var id = Guid.CreateVersion7();
        loaded[id] = stored.Passed;
        return new UnannouncedEvent(id, stored.Passed.Examination.Value, stored.StreamVersion, stored.StoredAt);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, int Limit);
}
