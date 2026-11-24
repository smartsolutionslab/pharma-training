using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;

/// <summary>
/// The stored ExaminationPassed events without a marker (#277): one anti-join of Marten's events against
/// <c>announced_events</c>, both in the module's schema of its database, oldest first (<see cref="UnannouncedEvents"/>);
/// each found event is then loaded on its own through Marten, so its data is read as Marten stored it. It takes the store
/// alone, so the module's event store registers it once.
/// </summary>
public sealed class UnannouncedTestPasses(IDocumentStore store) : IUnannouncedTestPasses
{
    /// <summary>The module's event store and its tables live in this schema.</summary>
    public const string Schema = AssessmentAggregateStore.Schema;

    /// <summary>Marten's stored name of <see cref="ExaminationPassed"/>.</summary>
    public const string SweptEventType = "examination_passed";

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken) =>
        UnannouncedEvents.FindAsync(store, Schema, SweptEventType, storedFrom, storedUntil, after, limit, cancellationToken);

    public async Task<ExaminationPassed> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        Ensure.That(found).IsNotNull();

        await using var session = store.QuerySession();
        var stored = await session.Events.LoadAsync(found.EventId, cancellationToken)
            ?? throw new InvalidOperationException($"Assessment's event store lost event {found.EventId} between finding and loading it.");
        return (ExaminationPassed)stored.Data;
    }
}
