using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

/// <summary>
/// The stored ContentCompleted events without a marker (#277): one anti-join of Marten's events against
/// <c>announced_events</c>, both in the module's schema of its database, oldest first (<see cref="UnannouncedEvents"/>);
/// each found event is then loaded on its own through Marten, so its data is read as Marten stored it. It takes the store
/// alone, so the module's event store registers it once.
/// </summary>
public sealed class UnannouncedContentCompletions(IDocumentStore store) : IUnannouncedContentCompletions
{
    /// <summary>The module's event store and its tables live in this schema.</summary>
    public const string Schema = LearningAggregateStore.Schema;

    /// <summary>Marten's stored name of <see cref="ContentCompleted"/>.</summary>
    public const string SweptEventType = "content_completed";

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken) =>
        UnannouncedEvents.FindAsync(store, Schema, SweptEventType, storedFrom, storedUntil, after, limit, cancellationToken);

    /// <summary>
    /// A database failure Marten wraps once its command runs arrives as the <see cref="System.Data.Common.DbException"/>
    /// it wraps (<see cref="MartenDatabaseFailures"/>, #337), so the sweep ends the pass on it instead of skipping the
    /// event as unreadable.
    /// </summary>
    public Task<ContentCompleted> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        Ensure.That(found).IsNotNull();

        return MartenDatabaseFailures.ReadAsync(async () =>
        {
            await using var session = store.QuerySession();
            var stored = await session.Events.LoadAsync(found.EventId, cancellationToken)
                ?? throw new InvalidOperationException($"Learning's event store lost event {found.EventId} between finding and loading it.");
            return (ContentCompleted)stored.Data;
        });
    }
}
