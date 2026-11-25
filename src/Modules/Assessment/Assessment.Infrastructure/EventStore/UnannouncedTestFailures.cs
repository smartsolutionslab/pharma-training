using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;

/// <summary>
/// The stored failed attempts (AttemptSubmitted with a failed outcome; a passed one is found too, and marked by the sweep) without a marker (#307): one anti-join of Marten's events against <c>announced_events</c>, both in the
/// module's schema of its database, oldest first (<see cref="UnannouncedEvents"/>), through the partial index on their
/// stored time; each found event is then loaded on its own with its examination (<see cref="StoredExaminationEvents"/>).
/// It takes the store alone, so the module's event store registers it once.
/// </summary>
public sealed class UnannouncedTestFailures(IDocumentStore store) : IUnannouncedExaminationEvents<AttemptSubmitted>
{
    /// <summary>Marten's stored name of <see cref="AttemptSubmitted"/>.</summary>
    public const string SweptEventType = "attempt_submitted";

    public Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken) =>
        UnannouncedEvents.FindAsync(store, AssessmentAggregateStore.Schema, SweptEventType, storedFrom, storedUntil, after, limit, cancellationToken);

    public Task<StoredExaminationEvent<AttemptSubmitted>> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken) =>
        StoredExaminationEvents.LoadAsync<AttemptSubmitted>(store, found, cancellationToken);
}
