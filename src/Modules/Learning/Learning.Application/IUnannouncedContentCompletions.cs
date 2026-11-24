using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// The stored ContentCompleted events that have no marker in <c>announced_events</c> (#277): their announcement never
/// left. Implemented in the module's Infrastructure project over its event store.
/// </summary>
public interface IUnannouncedContentCompletions
{
    /// <summary>Those stored from <paramref name="storedFrom"/> to <paramref name="storedUntil"/>, oldest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StoredContentCompletion>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken);
}
