using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Lets one MigrationRunner at a time change a module's database (docs/adr/0005). Published, the runner is each replica's
/// init container, so two replicas starting together run it at once: the second waits here, on a session advisory lock of
/// the database's own taken on a connection of its own, until the first has migrated, applied the event store's schema and
/// ensured the module's role, and then finds nothing left to do. Disposing it lets the next one in.
/// </summary>
public sealed class MigrationGate : IAsyncDisposable
{
    // Schema-qualified, with the seed typed as pg_catalog's own (text, bigint), besides the session's pinned search_path: a
    // module role may plant a hashtextextended(text, integer) in a schema named after the administrator, an exact match for
    // an integer literal that would win over pg_catalog's and run as the administrator (CVE-2018-1058, AdministratorSession).
    private const string Key = "pg_catalog.hashtextextended('pharma-training:migrations:' || pg_catalog.current_database(), 0::bigint)";

    private const string Enter = $"SELECT pg_catalog.pg_advisory_lock({Key})";

    private const string Leave = $"SELECT pg_catalog.pg_advisory_unlock({Key})";

    private readonly NpgsqlConnection connection;

    private MigrationGate(NpgsqlConnection connection) => this.connection = connection;

    /// <summary>Waits until no other run holds the database named by <paramref name="administrator"/>, then holds it.</summary>
    public static async Task<MigrationGate> EnterAsync(string administrator, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();

        // Unpooled: the lock belongs to the session, which must end with the gate.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(AdministratorSession.Pinned(administrator)) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var enter = new NpgsqlCommand(Enter, connection);
            await enter.ExecuteNonQueryAsync(cancellationToken);
            return new MigrationGate(connection);
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
        finally
        {
            // Closing the session releases the lock too, should the unlock above have failed.
            await connection.DisposeAsync();
        }
    }
}
