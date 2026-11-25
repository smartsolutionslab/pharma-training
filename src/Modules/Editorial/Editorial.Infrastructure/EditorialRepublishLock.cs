using System.Data;
using System.Data.Common;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>
/// The republish's lock (ADR 0019): a PostgreSQL session-level advisory lock under <see cref="Key"/> in Editorial's
/// database, taken with <c>pg_try_advisory_lock</c>, which answers at once. The run's DbContext opens its connection
/// for the scope and keeps it open until the lock is released, so the run's loads, its outbox transactions and the lock
/// share one session; a transaction-level lock could not span the run's commits, one per version. The lock is released
/// with <c>pg_advisory_unlock</c> when the run disposes it, and by PostgreSQL when the session ends any other way.
/// <para>
/// That session is the run's own: before it opens the connection, the lock points the scope's context at the same
/// database with <c>Pooling=false</c>, and points it back at the pooled connection string once it closed it. So closing
/// it, on every path, ends the session and with it everything session-level: a lock that a cancelled
/// <c>pg_try_advisory_lock</c> took on the server before the cancel landed, or one a failed rollback or unlock left
/// held, never goes back to Npgsql's pool, where it would stay held on an idle connection, refusing every run, until
/// the pool pruned it minutes later (Npgsql resets a pooled connection only when it is next used). That is simpler
/// than releasing everything with <c>pg_advisory_unlock_all</c> before each close, which cannot run in a failed
/// transaction or on a broken connection either; the price is one physical connection opened per run, and a republish
/// is rare. The loads and the outbox go through the same context, so they stay on the lock's session as before.
/// </para>
/// </summary>
public sealed partial class EditorialRepublishLock(EditorialDbContext context, ILogger<EditorialRepublishLock> logger) : IRepublishLock
{
    /// <summary>
    /// The advisory lock's key, the ASCII of "Republis" as one bigint, far from the small keys Wolverine takes in the
    /// same database. The integration tests hold it to make a run wait.
    /// </summary>
    public const long Key = 0x5265_7075_626C_6973;

    /// <summary>
    /// Takes the lock on a session of the run's own. It must be the scope's first use of the context's connection: it
    /// points the context at an unpooled connection string, which cannot change while the connection is open, and a
    /// connection the scope opened before would be a pooled session the lock could not end with the run.
    /// </summary>
    public async Task<Option<IAsyncDisposable>> TryTakeAsync(CancellationToken cancellationToken)
    {
        var pooled = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Editorial's context has no connection string to take the republish's lock on.");
        context.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(pooled) { Pooling = false }.ConnectionString);
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }
        catch
        {
            context.Database.SetConnectionString(pooled);
            throw;
        }

        try
        {
            var taken = await context.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({Key}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!taken)
            {
                await CloseAsync(context, pooled);
                return Option<IAsyncDisposable>.None;
            }

            return Option<IAsyncDisposable>.Some(new Held(context, pooled, logger));
        }
        catch
        {
            await CloseAsync(context, pooled);
            throw;
        }
    }

    /// <summary>
    /// Ends the run's own session, and everything it holds, and points the context back at the pool. A run that opened
    /// the connection once more and left it open keeps it open after this close, and a connection string cannot change
    /// while it is: the context then stays on its own session, which disposing the scope, and with it the context, ends.
    /// </summary>
    private static async Task CloseAsync(EditorialDbContext context, string pooled)
    {
        await context.Database.CloseConnectionAsync();
        if (context.Database.GetDbConnection().State == ConnectionState.Closed)
        {
            context.Database.SetConnectionString(pooled);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The republish's lock could not be released on its session; closing the session releases it.")]
    private static partial void ReleaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The republish's lock's release broke on its session; closing the session releases it.")]
    private static partial void ReleaseBroke(ILogger logger, Exception exception);

    /// <summary>
    /// The lock while the run holds it; disposing it unlocks and closes the run's session. A commit that failed can
    /// leave its transaction open and aborted, where the unlock would fail, so that transaction is rolled back first: its
    /// save failed anyway. A rollback or unlock that fails all the same is never thrown out of the run's dispose: closing
    /// the session, which always follows, releases the lock (or, on a connection the run left open, disposing the scope
    /// does). A database error or a timeout is logged as a warning; anything else but running out of memory is a bug and
    /// is logged as an error, so a broken release stands apart from a slow one.
    /// </summary>
    private sealed class Held(EditorialDbContext context, string pooled, ILogger logger) : IAsyncDisposable
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
            catch (Exception exception) when (exception is DbException or TimeoutException)
            {
                // A database error, a timeout or a broken connection: the close below releases the lock.
                ReleaseFailed(logger, exception);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Anything else is a bug, logged apart from a slow or failing database; the close below still releases
                // the lock. Only running out of memory is not the run's dispose's to swallow.
                ReleaseBroke(logger, exception);
            }
            finally
            {
                await CloseAsync(context, pooled);
            }
        }
    }
}
