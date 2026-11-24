using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// The stored ContentCompleted events that have no marker in <c>announced_events</c> (#277): their announcement never
/// left. Implemented in the module's Infrastructure project over its event store.
/// </summary>
public interface IUnannouncedContentCompletions
{
    /// <summary>Those stored from <paramref name="storedFrom"/> to <paramref name="storedUntil"/>, oldest first, at most <paramref name="limit"/>; their data is not read.</summary>
    Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken);

    /// <summary>The stored completion <paramref name="found"/> names, as Marten stored it; throws when it cannot be read.</summary>
    Task<ContentCompleted> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken);
}
