using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>
/// The stored ExaminationPassed events that have no marker in <c>announced_events</c> (#277): their announcement never
/// left. Implemented in the module's Infrastructure project over its event store.
/// </summary>
public interface IUnannouncedTestPasses
{
    /// <summary>
    /// Those stored from <paramref name="storedFrom"/> to <paramref name="storedUntil"/> and after <paramref name="after"/>
    /// in the event store's sequence (<see cref="UnannouncedEvent.Sequence"/>), oldest first, at most
    /// <paramref name="limit"/>; their data is not read.
    /// </summary>
    Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken);

    /// <summary>The stored test pass <paramref name="found"/> names, as Marten stored it; throws when it cannot be read.</summary>
    Task<ExaminationPassed> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken);
}
