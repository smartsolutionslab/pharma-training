using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Makes a context's save end as PostgreSQL would have ended it, before anything reaches the database: the save throws
/// the given failure, or, without one, saves nothing and succeeds. <c>saved</c> is told when the save starts.
/// </summary>
public sealed class FailingSave(Exception? failure, Action? saved = null) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        saved?.Invoke();
        return failure is null
            ? ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0))
            : ValueTask.FromException<InterceptionResult<int>>(failure);
    }
}
