namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// One republish of Editorial's course-version events at a time (ADR 0019). The module's Infrastructure implements it
/// over a PostgreSQL advisory lock in Editorial's database, so it holds across every instance of the service.
/// </summary>
public interface IRepublishLock
{
    /// <summary>
    /// Takes the lock for this run, or answers none, at once and without waiting, when another run holds it. Disposing
    /// the answer releases it; a run that ends any other way (its connection lost, its process gone) releases it too.
    /// </summary>
    Task<Option<IAsyncDisposable>> TryTakeAsync(CancellationToken cancellationToken);
}
