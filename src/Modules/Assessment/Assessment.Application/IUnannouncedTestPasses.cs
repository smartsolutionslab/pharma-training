using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>
/// The stored ExaminationPassed events that have no marker in <c>announced_events</c> (#277): their announcement never
/// left. Implemented in the module's Infrastructure project over its event store.
/// </summary>
public interface IUnannouncedTestPasses
{
    /// <summary>Those stored from <paramref name="storedFrom"/> to <paramref name="storedUntil"/>, oldest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StoredTestPass>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken);
}
