using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Lets one MigrationRunner at a time change a module's database (docs/adr/0005). Published, the runner is each replica's
/// init container, so two replicas starting together run it at once: the second waits here, on a session advisory lock of
/// the database's own taken on a connection of its own, until the first has migrated, applied the event store's schema and
/// ensured the module's role, and then finds nothing left to do, however long that takes. Disposing it lets the next one in;
/// when the session is gone already (the server ended it, the network dropped), the server has released the lock with it,
/// and leaving logs that and never throws over the run's own failure.
/// The second asks for the lock once a second with <c>pg_try_advisory_lock</c> rather than waiting in
/// <c>pg_advisory_lock</c> (#337): a statement waiting for the lock holds a snapshot, and a concurrent index build of the
/// first run (<c>SweptEventIndex</c>) waits for every older snapshot of the database, so the two runs would wait for each
/// other for good. Between its tries the second's session holds nothing.
/// </summary>
public sealed partial class MigrationGate : IAsyncDisposable
{
    // Schema-qualified, with the seed typed as pg_catalog's own (text, bigint), besides the session's pinned search_path: a
    // module role may plant a hashtextextended(text, integer) in a schema named after the administrator, an exact match for
    // an integer literal that would win over pg_catalog's and run as the administrator (CVE-2018-1058, AdministratorSession).
    private const string Key = "pg_catalog.hashtextextended('pharma-training:migrations:' || pg_catalog.current_database(), 0::bigint)";

    private const string TryEnter = $"SELECT pg_catalog.pg_try_advisory_lock({Key})";

    private const string Leave = $"SELECT pg_catalog.pg_advisory_unlock({Key})";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly NpgsqlConnection connection;

    private readonly ILogger logger;

    private MigrationGate(NpgsqlConnection connection, ILogger logger)
    {
        this.connection = connection;
        this.logger = logger;
    }

    /// <summary>Waits until no other run holds the database named by <paramref name="administrator"/>, then holds it.</summary>
    public static Task<MigrationGate> EnterAsync(string administrator, CancellationToken cancellationToken) =>
        EnterAsync(administrator, NullLogger.Instance, cancellationToken);

    /// <summary>
    /// As <see cref="EnterAsync(string, CancellationToken)"/>, logging to <paramref name="logger"/> once that it waits for
    /// another run, and an unlock that failed.
    /// </summary>
    public static Task<MigrationGate> EnterAsync(string administrator, ILogger logger, CancellationToken cancellationToken) =>
        EnterAsync(administrator, logger, TimeProvider.System, cancellationToken);

    /// <summary>As <see cref="EnterAsync(string, ILogger, CancellationToken)"/>, waiting between tries on <paramref name="time"/>.</summary>
    public static async Task<MigrationGate> EnterAsync(string administrator, ILogger logger, TimeProvider time, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();
        Ensure.That(logger, nameof(logger)).IsNotNull();
        Ensure.That(time, nameof(time)).IsNotNull();

        // Unpooled: the lock belongs to the session, which must end with the gate.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(AdministratorSession.Pinned(administrator)) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            // However long the other run migrates: each try returns at once, so the connection's command timeout never
            // ends the wait; cancellation may.
            await using var enter = new NpgsqlCommand(TryEnter, connection);
            var waiting = false;
            while (await enter.ExecuteScalarAsync(cancellationToken) is not true)
            {
                // Once, not per try (#351): an init container's log then says why it has not finished.
                if (!waiting)
                {
                    Waiting(logger);
                    waiting = true;
                }

                await Task.Delay(RetryDelay, time, cancellationToken);
            }

            return new MigrationGate(connection, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var leave = new NpgsqlCommand(Leave, connection);
            await leave.ExecuteNonQueryAsync();
        }
        catch (Exception failure) when (failure is NpgsqlException or InvalidOperationException)
        {
            // The session is gone, and its lock with it. Throwing here would replace the run's own failure, which is what
            // the init container's log has to show.
            UnlockFailed(logger, failure);
        }
        finally
        {
            // Closing the session releases the lock too, should the unlock above have failed.
            await connection.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for another run at the migration gate: another replica's MigrationRunner is changing this database; trying again every second until it is done.")]
    private static partial void Waiting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leaving the migration gate could not unlock it; closing the session releases the lock.")]
    private static partial void UnlockFailed(ILogger logger, Exception failure);
}
