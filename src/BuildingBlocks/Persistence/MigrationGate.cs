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
    private const string Key = "hashtextextended('pharma-training:migrations:' || current_database(), 0)";

    private const string Enter = $"SELECT pg_advisory_lock({Key})";

    private const string Leave = $"SELECT pg_advisory_unlock({Key})";

    private readonly NpgsqlConnection connection;

    private MigrationGate(NpgsqlConnection connection) => this.connection = connection;

    /// <summary>Waits until no other run holds the database named by <paramref name="administrator"/>, then holds it.</summary>
    public static async Task<MigrationGate> EnterAsync(string administrator, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();

        // Unpooled: the lock belongs to the session, which must end with the gate.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(administrator) { Pooling = false }.ConnectionString);
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
