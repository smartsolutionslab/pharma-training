using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>
/// The republish's lock (ADR 0019): a PostgreSQL session-level advisory lock under <see cref="Key"/> in Editorial's
/// database, taken with <c>pg_try_advisory_lock</c>, which answers at once. The run's DbContext opens its connection
/// for the scope and keeps it open until the lock is released, so the run's loads, its outbox transactions and the lock
/// share one session; a transaction-level lock could not span the run's commits, one per version. The lock is released
/// with <c>pg_advisory_unlock</c> when the run disposes it, and by PostgreSQL when the session ends any other way.
/// </summary>
public sealed class EditorialRepublishLock(EditorialDbContext context) : IRepublishLock
{
    /// <summary>
    /// The advisory lock's key, the ASCII of "Republis" as one bigint, far from the small keys Wolverine takes in the
    /// same database. The integration tests hold it to make a run wait.
    /// </summary>
    public const long Key = 0x5265_7075_626C_6973;

    public async Task<Option<IAsyncDisposable>> TryTakeAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var taken = await context.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({Key}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!taken)
            {
                await context.Database.CloseConnectionAsync();
                return Option<IAsyncDisposable>.None;
            }

            return Option<IAsyncDisposable>.Some(new Held(context));
        }
        catch
        {
            await context.Database.CloseConnectionAsync();
            throw;
        }
    }

    /// <summary>
    /// The lock while the run holds it; disposing it unlocks and closes the scope's connection. A commit that failed can
    /// leave its transaction open and aborted, where the unlock would fail, so that transaction is rolled back first: its
    /// save failed anyway. Npgsql resets a pooled connection only when it is next used, so an unlock that did not run
    /// would leave the lock held on an idle connection.
    /// </summary>
    private sealed class Held(EditorialDbContext context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (context.Database.CurrentTransaction is not null)
                {
                    await context.Database.RollbackTransactionAsync(CancellationToken.None);
                }

                await context.Database
                    .SqlQuery<bool>($"SELECT pg_advisory_unlock({Key}) AS \"Value\"")
                    .SingleAsync(CancellationToken.None);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
