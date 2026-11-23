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
/// </summary>
public sealed partial class MigrationGate : IAsyncDisposable
{
    // Schema-qualified, with the seed typed as pg_catalog's own (text, bigint), besides the session's pinned search_path: a
    // module role may plant a hashtextextended(text, integer) in a schema named after the administrator, an exact match for
    // an integer literal that would win over pg_catalog's and run as the administrator (CVE-2018-1058, AdministratorSession).
    private const string Key = "pg_catalog.hashtextextended('pharma-training:migrations:' || pg_catalog.current_database(), 0::bigint)";

    private const string Enter = $"SELECT pg_catalog.pg_advisory_lock({Key})";

    private const string Leave = $"SELECT pg_catalog.pg_advisory_unlock({Key})";

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

    /// <summary>As <see cref="EnterAsync(string, CancellationToken)"/>, logging to <paramref name="logger"/> an unlock that failed.</summary>
    public static async Task<MigrationGate> EnterAsync(string administrator, ILogger logger, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();
        Ensure.That(logger, nameof(logger)).IsNotNull();

        // Unpooled: the lock belongs to the session, which must end with the gate.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(AdministratorSession.Pinned(administrator)) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            // However long the other run migrates: the connection's command timeout must not end the wait, cancellation may.
            await using var enter = new NpgsqlCommand(Enter, connection) { CommandTimeout = 0 };
            await enter.ExecuteNonQueryAsync(cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leaving the migration gate could not unlock it; closing the session releases the lock.")]
    private static partial void UnlockFailed(ILogger logger, Exception failure);
}
